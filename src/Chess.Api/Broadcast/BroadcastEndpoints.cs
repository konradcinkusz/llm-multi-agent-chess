using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Broadcast;

/// <summary>
/// One slot as the planner page sends it to <c>PUT /api/broadcast/schedules/{id}/slots</c>. The array order is the
/// play order; the slot index is assigned from it.
/// </summary>
public sealed record SlotEdit(PlayerSpec White, PlayerSpec Black, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd);

/// <summary>
/// The <c>/api/broadcast</c> routes (03 §9.2): plan a day (saved as a draft), list and read schedules, replace the slots
/// of a draft or scheduled schedule, and <c>/now</c> for the broadcast page and OBS. Nothing here plays a game; the
/// control routes hand the schedule to the broadcast runner.
/// </summary>
public static class BroadcastEndpoints
{
    /// <summary>
    /// The plies the premium estimate assumes when a stored schedule is read. The plan request carries
    /// <c>AssumedPlies</c>, but 03 §9.3 does not store it, so reads use the 03 §9.1 assumption.
    /// </summary>
    public const double StoredAssumedPlies = 80;

    /// <summary>The longest window a plan may cover. A week of slots is already far beyond one broadcast day.</summary>
    public const int MaxDurationHours = 168;

    private const string LlmKind = "llm";
    private const string RandomKind = "random";

    public static IEndpointRouteBuilder MapBroadcastEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var broadcast = endpoints.MapGroup("/broadcast");
        broadcast.MapPost("/plan", PlanAsync);
        broadcast.MapGet("/schedules", ListAsync);
        broadcast.MapGet("/schedules/{id:guid}", GetAsync);
        broadcast.MapPut("/schedules/{id:guid}/slots", ReplaceSlotsAsync);
        broadcast.MapGet("/now", NowAsync);
        broadcast.MapPost("/schedules/{id:guid}/start", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CancellationToken ct) =>
            CommandAsync(id, controller.StartAsync, factory, catalog, ct));
        broadcast.MapPost("/schedules/{id:guid}/pause", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CancellationToken ct) =>
            CommandAsync(id, controller.PauseAsync, factory, catalog, ct));
        broadcast.MapPost("/schedules/{id:guid}/resume", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CancellationToken ct) =>
            CommandAsync(id, controller.ResumeAsync, factory, catalog, ct));
        broadcast.MapPost("/schedules/{id:guid}/stop", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CancellationToken ct) =>
            CommandAsync(id, controller.StopAsync, factory, catalog, ct));
        return endpoints;
    }

    private static async Task<IResult> PlanAsync(
        PlanRequest? request,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid plan", "A plan request is required.");
        }

        if (request.Models is null || request.Pacing is null)
        {
            return ValidationProblemFor("request", "Models and pacing are required.");
        }

        if (request.DurationHours > MaxDurationHours)
        {
            return ValidationProblemFor("durationHours",
                $"DurationHours must be at most {MaxDurationHours}.");
        }

        var listing = await catalog.GetModelsAsync(ct);
        var multipliers = Multipliers(listing);

        BroadcastPlan plan;
        try
        {
            plan = BroadcastPlanner.Plan(request, multipliers);
        }
        catch (InvalidPlanException ex)
        {
            return ValidationProblemFor("request", ex.Message);
        }

        // With the gateway down only the random player can be checked; a draft may still name LLMs.
        if (listing.Warning is null)
        {
            var models = ModelErrors(request.Models, listing);
            if (models is not null)
            {
                return TypedResults.ValidationProblem(models);
            }
        }

        var distinctModels = request.Models.Distinct(StringComparer.Ordinal).ToList();
        var schedule = new BroadcastScheduleEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Status = ScheduleStatus.Draft,
            StartsAt = request.StartsAt,
            EndsAt = request.StartsAt.AddHours(request.DurationHours),
            MoveDelayMs = request.Pacing.MoveDelayMs,
            GameGapSeconds = request.Pacing.GameGapSeconds,
            Seed = request.Seed,
            Models = distinctModels,
            CreatedAt = clock.GetUtcNow(),
        };
        var slots = plan.Slots.Select(slot => ToSlotEntity(schedule.Id, slot)).ToList();

        await using var db = await factory.CreateDbContextAsync(ct);
        db.BroadcastSchedules.Add(schedule);
        db.BroadcastSlots.AddRange(slots);
        await db.SaveChangesAsync(ct);

        var detail = new ScheduleDetailDto(
            ToScheduleDto(schedule, slots.Count, slots.Count(s => s.State == SlotState.Finished),
                plan.EstimatedPremiumRequests),
            slots.OrderBy(s => s.Index).Select(ToSlotDto).ToList());
        return TypedResults.Created($"/api/broadcast/schedules/{schedule.Id}", detail);
    }

    private static async Task<IResult> ListAsync(
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CancellationToken ct)
    {
        var multipliers = Multipliers(await catalog.GetModelsAsync(ct));

        await using var db = await factory.CreateDbContextAsync(ct);
        var schedules = await db.BroadcastSchedules.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var slots = await db.BroadcastSlots.AsNoTracking().ToListAsync(ct);

        var dtos = schedules.Select(schedule =>
        {
            var own = slots.Where(s => s.ScheduleId == schedule.Id).ToList();
            return ToScheduleDto(schedule, own, multipliers);
        }).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CancellationToken ct)
    {
        var multipliers = Multipliers(await catalog.GetModelsAsync(ct));

        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (loaded is null)
        {
            return NotFound(id);
        }

        var (schedule, slots) = loaded.Value;
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, slots, multipliers),
            slots.Select(ToSlotDto).ToList()));
    }

    private static async Task<IResult> ReplaceSlotsAsync(
        Guid id,
        IReadOnlyList<SlotEdit>? edits,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (loaded is null)
        {
            return NotFound(id);
        }

        var (schedule, existing) = loaded.Value;
        if (schedule.Status is not (ScheduleStatus.Draft or ScheduleStatus.Scheduled))
        {
            return Conflict(schedule.Status, "Slots can only be edited while the schedule is a draft or scheduled.");
        }

        if (edits is null || edits.Count == 0)
        {
            return ValidationProblemFor("slots", "At least one slot is required.");
        }

        var errors = SlotErrors(edits, schedule);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var replaced = edits.Select((edit, index) => new BroadcastSlotEntity
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            Index = index,
            WhiteKind = edit.White.Kind,
            WhiteModel = edit.White.ModelId,
            BlackKind = edit.Black.Kind,
            BlackModel = edit.Black.ModelId,
            PlannedStart = edit.PlannedStart,
            PlannedEnd = edit.PlannedEnd,
            State = SlotState.Pending,
        }).ToList();

        // Two saves: the (schedule, index) key is unique, so the old rows must be gone before the new ones are inserted.
        db.BroadcastSlots.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        db.BroadcastSlots.AddRange(replaced);
        await db.SaveChangesAsync(ct);

        var multipliers = Multipliers(await catalog.GetModelsAsync(ct));
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, replaced, multipliers),
            replaced.Select(ToSlotDto).ToList()));
    }

    /// <summary>
    /// The controller owns what the broadcast page shows (its status, the game in play, the next slots and the
    /// technical-break notice), so this route passes its answer through.
    /// </summary>
    private static async Task<IResult> NowAsync(IBroadcastController controller, CancellationToken ct) =>
        TypedResults.Ok(await controller.NowAsync(ct));

    /// <summary>
    /// Runs a schedule command on the controller and answers with the schedule as stored: 200 when accepted, 404 when
    /// the schedule does not exist, 409 when its status does not allow the command.
    /// </summary>
    private static async Task<IResult> CommandAsync(Guid id,
        Func<Guid, CancellationToken, Task<BroadcastCommandResult>> command,
        IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CancellationToken ct)
    {
        var result = await command(id, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (result == BroadcastCommandResult.NotFound || loaded is null)
        {
            return NotFound(id);
        }

        if (result == BroadcastCommandResult.Conflict)
        {
            return Conflict(loaded.Value.Schedule.Status, "That command does not apply to a schedule in this state.");
        }

        var multipliers = Multipliers(await catalog.GetModelsAsync(ct));
        var (schedule, slots) = loaded.Value;
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, slots, multipliers),
            slots.Select(ToSlotDto).ToList()));
    }

    private static async Task<(BroadcastScheduleEntity Schedule, List<BroadcastSlotEntity> Slots)?> LoadAsync(
        ArenaDbContext db, Guid id, CancellationToken ct)
    {
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return null;
        }

        var slots = await db.BroadcastSlots.Where(s => s.ScheduleId == id).OrderBy(s => s.Index).ToListAsync(ct);
        return (schedule, slots);
    }

    /// <summary>Checks the model names of a plan against the gateway's list, as game creation does (03 §5).</summary>
    private static Dictionary<string, string[]>? ModelErrors(IEnumerable<string> models, ModelListing listing)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var model in models.Distinct(StringComparer.Ordinal))
        {
            if (model == ModelCatalog.RandomModelId)
            {
                continue;
            }

            var listed = listing.Models.FirstOrDefault(m => m.Id == model);
            if (listed is null)
            {
                errors["models"] = [$"The model '{model}' is not one the gateway offers."];
                return errors;
            }

            if (!listed.Enabled)
            {
                errors["models"] = [$"The model '{model}' is not enabled."];
                return errors;
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>Checks that the edited slots are playable: known players, a proper window, in play order, no overlap.</summary>
    private static Dictionary<string, string[]>? SlotErrors(IReadOnlyList<SlotEdit> edits, BroadcastScheduleEntity schedule)
    {
        var errors = new List<string>();
        DateTimeOffset? previousEnd = null;

        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var label = $"Slot {index + 1}";
            if (edit.White is null || edit.Black is null)
            {
                errors.Add($"{label}: both players are required.");
                continue;
            }

            foreach (var (side, player) in new[] { ("White", edit.White), ("Black", edit.Black) })
            {
                if (player.Kind is not (LlmKind or RandomKind))
                {
                    errors.Add($"{label}: the {side} player's kind must be '{LlmKind}' or '{RandomKind}'.");
                }
                else if ((player.Kind == RandomKind) != (player.ModelId == ModelCatalog.RandomModelId))
                {
                    errors.Add($"{label}: the {side} random player's model must be '{ModelCatalog.RandomModelId}'.");
                }
                else if (!schedule.Models.Contains(player.ModelId, StringComparer.Ordinal))
                {
                    errors.Add($"{label}: the {side} model '{player.ModelId}' is not one of the schedule's models.");
                }
            }

            if (edit.White == edit.Black)
            {
                errors.Add($"{label}: a player cannot play itself.");
            }

            if (edit.PlannedEnd <= edit.PlannedStart)
            {
                errors.Add($"{label}: the slot must end after it starts.");
            }

            if (edit.PlannedStart < schedule.StartsAt || edit.PlannedEnd > schedule.EndsAt)
            {
                errors.Add($"{label}: the slot must lie inside the schedule window.");
            }

            if (previousEnd is { } end && edit.PlannedStart < end)
            {
                errors.Add($"{label}: the slot starts before the previous one ends; slots play one at a time in order.");
            }

            previousEnd = edit.PlannedEnd;
        }

        return errors.Count == 0 ? null : new Dictionary<string, string[]> { ["slots"] = errors.ToArray() };
    }

    private static IReadOnlyDictionary<string, double?> Multipliers(ModelListing listing) =>
        listing.Models.ToDictionary(m => m.Id, m => m.PremiumMultiplier, StringComparer.Ordinal);

    private static BroadcastSlotEntity ToSlotEntity(Guid scheduleId, PlannedSlot slot) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = scheduleId,
        Index = slot.Index,
        WhiteKind = slot.White.Kind,
        WhiteModel = slot.White.ModelId,
        BlackKind = slot.Black.Kind,
        BlackModel = slot.Black.ModelId,
        PlannedStart = slot.PlannedStart,
        PlannedEnd = slot.PlannedEnd,
        State = SlotState.Pending,
    };

    private static SlotDto ToSlotDto(BroadcastSlotEntity slot) => new(slot.Id, slot.Index,
        new PlayerSpec(slot.WhiteKind, slot.WhiteModel), new PlayerSpec(slot.BlackKind, slot.BlackModel),
        slot.PlannedStart, slot.PlannedEnd, slot.State, slot.GameId);

    /// <summary>The schedule as the API shows it. Plans pass the estimate the planner computed for the request.</summary>
    private static ScheduleDto ToScheduleDto(BroadcastScheduleEntity schedule, int slotsTotal, int slotsFinished,
        IReadOnlyList<ModelPremiumEstimateDto> estimate) =>
        new(schedule.Id, schedule.Name, schedule.Status, schedule.StartsAt, schedule.EndsAt,
            new PacingProfile(schedule.MoveDelayMs, schedule.GameGapSeconds), schedule.Seed, schedule.Models,
            slotsTotal, slotsFinished, estimate);

    /// <summary>The schedule as stored: the estimate is recomputed from its slots (see <see cref="StoredAssumedPlies"/>).</summary>
    private static ScheduleDto ToScheduleDto(BroadcastScheduleEntity schedule, IReadOnlyList<BroadcastSlotEntity> slots,
        IReadOnlyDictionary<string, double?> multipliers) =>
        ToScheduleDto(schedule, slots.Count, slots.Count(s => s.State == SlotState.Finished),
            schedule.Models.Select(model => new ModelPremiumEstimateDto(model, EstimateFor(model, slots, multipliers)))
                .ToList());

    // slots-per-model × (plies / 2) moves per side × multiplier; the random player costs nothing (as the planner).
    private static double? EstimateFor(string model, IReadOnlyList<BroadcastSlotEntity> slots,
        IReadOnlyDictionary<string, double?> multipliers)
    {
        if (model == ModelCatalog.RandomModelId)
        {
            return 0;
        }

        if (!multipliers.TryGetValue(model, out var multiplier) || multiplier is not { } value)
        {
            return null;
        }

        var slotsForModel = slots.Count(slot => slot.WhiteModel == model || slot.BlackModel == model);
        return slotsForModel * (StoredAssumedPlies / 2) * value;
    }

    private static ValidationProblem ValidationProblemFor(string key, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);

    private static ProblemHttpResult NotFound(Guid id) => Problem(StatusCodes.Status404NotFound,
        "Schedule not found", $"No broadcast schedule has the id {id}.");

    private static ProblemHttpResult Conflict(string status, string detail) => Problem(StatusCodes.Status409Conflict,
        "Schedule status does not allow this", $"The schedule is {status}. {detail}");
}
