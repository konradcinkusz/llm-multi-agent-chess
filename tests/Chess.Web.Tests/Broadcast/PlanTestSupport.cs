using System.Net;
using System.Text.Json;
using ApiBroadcast = Chess.Api.Broadcast;
using ApiTournaments = Chess.Api.Tournaments;
using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Chess.Web.Tests.Tournaments;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests.Broadcast;

/// <summary>
/// A stand-in for the <c>/api/broadcast</c> routes that behaves like the API where the planner page can see it: plans
/// come from the real <c>Chess.Api</c> planner, and schedule commands follow the controller's state rules (409 when
/// refused). Every request is recorded, so tests can assert what was sent and what was not.
/// </summary>
internal sealed class FakeBroadcast
{
    /// <summary>
    /// What the fake API lists: gpt is a multiplier-1 model priced at $3 / $15 per 1M tokens, claude a multiplier-1.5 model
    /// without prices, random is free. The planner and the stored schedule estimate from these, as the real API does.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ApiTournaments.ModelCostInputs> Inputs =
        new Dictionary<string, ApiTournaments.ModelCostInputs>(StringComparer.Ordinal)
        {
            ["gpt"] = new(1, new(3.0, 15.0, null), null),
            ["claude"] = new(1.5, null, null),
            ["random"] = new(null, null, null),
        };

    /// <summary>What the API charges per slot for one model: 80 plies, half a move a side (03 §9.1).</summary>
    private const double StoredAssumedPlies = 80;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<Guid, Stored> _schedules = [];

    public FakeBroadcast() => Handler = new FakeHandler(Answer);

    public FakeHandler Handler { get; }

    public IReadOnlyList<RecordedRequest> Requests => Handler.Requests;

    public Guid SeedSchedule(string status, string name = "Spring day")
    {
        var id = Guid.NewGuid();
        var slots = new List<SlotDto>
        {
            Slot(0, "gpt", "claude", new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero)),
            Slot(1, "claude", "gpt", new DateTimeOffset(2026, 10, 24, 11, 0, 0, TimeSpan.Zero)),
        };
        _schedules[id] = new Stored(name, status, slots);
        return id;
    }

    public void SetStatus(Guid id, string status) => _schedules[id] = _schedules[id] with { Status = status };

    public string StatusOf(Guid id) => _schedules[id].Status;

    private HttpResponseMessage Answer(RecordedRequest request)
    {
        var path = request.Target.Split('?')[0];
        var parts = path.Trim('/').Split('/');

        if (request.Method == "GET" && path == "/api/models")
        {
            return Canned.Json(Fixtures.Models);
        }

        if (request.Method == "POST" && path == "/api/broadcast/plan")
        {
            return Plan(request.Body!);
        }

        if (request.Method == "GET" && path == "/api/broadcast/schedules")
        {
            return Canned.Json(JsonSerializer.Serialize(_schedules.Select(pair => Dto(pair.Key, pair.Value)).ToList(), Json));
        }

        // /api/broadcast/schedules/{id}[/{action}]
        if (parts.Length >= 4 && parts[0] == "api" && parts[1] == "broadcast" && parts[2] == "schedules"
            && Guid.TryParse(parts[3], out var id) && _schedules.TryGetValue(id, out var stored))
        {
            var action = parts.Length == 5 ? parts[4] : "";
            if (request.Method == "PUT" && action == "slots")
            {
                return ReplaceSlots(id, stored, request.Body!);
            }

            if (request.Method == "POST" && action is "start" or "pause" or "resume" or "stop")
            {
                return Command(id, stored, action);
            }
        }

        return Canned.Json("{}", HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Plan(string body)
    {
        var plan = JsonSerializer.Deserialize<ApiBroadcast.PlanRequest>(body, Json)!;
        var planned = ApiBroadcast.BroadcastPlanner.Plan(plan, Inputs, new ApiTournaments.CostEstimateOptions());
        var id = Guid.NewGuid();
        var slots = planned.Slots.Select(slot => new SlotDto(Guid.NewGuid(), slot.Index,
            new PlayerSpec(slot.White.Kind, slot.White.ModelId), new PlayerSpec(slot.Black.Kind, slot.Black.ModelId),
            slot.PlannedStart, slot.PlannedEnd, "pending", null)).ToList();
        _schedules[id] = new Stored(plan.Name.Trim(), "draft", slots);
        return new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(JsonSerializer.Serialize(Detail(id, _schedules[id]), Json), System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private HttpResponseMessage ReplaceSlots(Guid id, Stored stored, string body)
    {
        if (stored.Status is not ("draft" or "scheduled"))
        {
            return Refused(stored.Status, "Slots can only be edited while the schedule is a draft or scheduled.");
        }

        var edits = JsonSerializer.Deserialize<List<SlotEdit>>(body, Json)!;
        var slots = edits.Select((edit, index) => new SlotDto(Guid.NewGuid(), index, edit.White, edit.Black,
            edit.PlannedStart, edit.PlannedEnd, "pending", null)).ToList();
        _schedules[id] = stored with { Slots = slots };
        return Canned.Json(JsonSerializer.Serialize(Detail(id, _schedules[id]), Json));
    }

    private HttpResponseMessage Command(Guid id, Stored stored, string action)
    {
        var next = (action, stored.Status) switch
        {
            ("start", "draft" or "scheduled") when !OtherChannelBusy(id) => "running",
            ("pause", "running") => "paused",
            ("resume", "paused") => "running",
            ("stop", "scheduled" or "running" or "paused") => "cancelled",
            _ => null,
        };
        if (next is null)
        {
            return Refused(stored.Status, "That command does not apply to a schedule in this state.");
        }

        _schedules[id] = stored with { Status = next };
        return Canned.Json(JsonSerializer.Serialize(Detail(id, _schedules[id]), Json));
    }

    private bool OtherChannelBusy(Guid except) =>
        _schedules.Any(pair => pair.Key != except && pair.Value.Status is "running" or "paused");

    private static HttpResponseMessage Refused(string status, string detail) => new(HttpStatusCode.Conflict)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            title = "Schedule status does not allow this",
            detail = $"The schedule is {status}. {detail}",
        }, Json), System.Text.Encoding.UTF8, "application/problem+json"),
    };

    private static SlotDto Slot(int index, string white, string black, DateTimeOffset start) => new(Guid.NewGuid(), index,
        new PlayerSpec("llm", white), new PlayerSpec("llm", black), start, start.AddMinutes(58), "pending", null);

    private static ScheduleDetailDto Detail(Guid id, Stored stored) => new(Dto(id, stored), stored.Slots);

    private static ScheduleDto Dto(Guid id, Stored stored)
    {
        var first = stored.Slots.FirstOrDefault();
        var starts = first?.PlannedStart ?? new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
        var ends = stored.Slots.Count == 0 ? starts : stored.Slots.Max(slot => slot.PlannedEnd);
        var models = stored.Slots.SelectMany(slot => new[] { slot.White.ModelId, slot.Black.ModelId }).Distinct().ToList();
        var estimate = models.Select(model => Estimate(model, stored.Slots)).ToList();
        return new ScheduleDto(id, stored.Name, stored.Status, starts, ends, new PacingProfile(4000, 20), 1, models,
            stored.Slots.Count, stored.Slots.Count(slot => slot.State == "finished"), estimate);
    }

    private static ModelPremiumEstimateDto Estimate(string model, IReadOnlyList<SlotDto> slots)
    {
        var estimate = ApiBroadcast.BroadcastPlanner.EstimateFor(model,
            slots.Count(slot => slot.White.ModelId == model || slot.Black.ModelId == model), StoredAssumedPlies,
            Inputs, new ApiTournaments.CostEstimateOptions());
        return new ModelPremiumEstimateDto(estimate.ModelId, estimate.EstimatedPremiumRequests,
            estimate.EstimatedAiCredits, estimate.EstimatedUsd, estimate.TokenBasis, estimate.SampleMoves);
    }

    private sealed record Stored(string Name, string Status, List<SlotDto> Slots);
}

/// <summary>Wires the page's two HTTP clients (models through <see cref="ArenaApiClient"/>, the rest through the broadcast client) to one fake.</summary>
internal static class PlanWire
{
    public static void Api(Bunit.BunitContext ctx, FakeBroadcast fake)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(fake.Handler)));
        ctx.Services.AddHttpClient(BroadcastApiClient.ClientName, client => client.BaseAddress = new Uri("http://api"))
            .ConfigurePrimaryHttpMessageHandler(() => fake.Handler);
    }
}
