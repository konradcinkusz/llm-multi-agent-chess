using System.Net;
using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Xunit;
using ApiBroadcast = Chess.Api.Broadcast;
using ApiContracts = Chess.Api.Contracts;
using ApiTournaments = Chess.Api.Tournaments;

namespace Chess.Web.Tests.Broadcast;

public class PlanApiClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset At = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EachMethodSendsItsVerbAndRoute()
    {
        var handler = new FakeHandler(_ => Canned.Json(Detail));
        var client = new BroadcastApiClient(Canned.Client(handler));
        var slots = new List<SlotEdit> { new(new PlayerSpec("llm", "gpt"), new PlayerSpec("llm", "claude"), At, At.AddMinutes(58)) };

        await client.PlanAsync(new PlanRequest("Day", ["gpt", "claude"], At, 24, new PacingProfile(4000, 20), 1, 80, 40_000));
        await client.ReplaceSlotsAsync(Id, slots);
        await client.StartAsync(Id);
        await client.PauseAsync(Id);
        await client.ResumeAsync(Id);
        await client.StopAsync(Id);

        var routes = handler.Requests.Select(request => $"{request.Method} {request.Target}").ToArray();
        Assert.Equal(
            [
                "POST /api/broadcast/plan",
                $"PUT /api/broadcast/schedules/{Id:D}/slots",
                $"POST /api/broadcast/schedules/{Id:D}/start",
                $"POST /api/broadcast/schedules/{Id:D}/pause",
                $"POST /api/broadcast/schedules/{Id:D}/resume",
                $"POST /api/broadcast/schedules/{Id:D}/stop",
            ],
            routes);
    }

    [Fact]
    public async Task ListAndDetailReadTheSchedules()
    {
        var handler = new FakeHandler(request => request.Target == "/api/broadcast/schedules"
            ? Canned.Json($"[{Schedule}]")
            : Canned.Json(Detail));
        var client = new BroadcastApiClient(Canned.Client(handler));

        var list = await client.GetSchedulesAsync();
        var detail = await client.GetScheduleAsync(Id);

        Assert.Equal("Spring day", list[0].Name);
        Assert.Equal("draft", detail.Schedule.Status);
        Assert.Equal(Id, detail.Schedule.Id);
    }

    [Fact]
    public async Task AProblemWithValidationMessagesSurfacesThoseMessages()
    {
        var handler = new FakeHandler(_ => Canned.Json(
            """{"title":"One or more validation errors occurred.","errors":{"slots":["Slot 1: the slot must end after it starts."]}}""",
            HttpStatusCode.BadRequest));
        var client = new BroadcastApiClient(Canned.Client(handler));

        var error = await Assert.ThrowsAsync<BroadcastApiException>(() => client.ReplaceSlotsAsync(Id, []));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("Slot 1: the slot must end after it starts.", error.Message);
    }

    [Fact]
    public async Task ARefusedCommandSurfacesTheProblemDetail()
    {
        var handler = new FakeHandler(_ => Canned.Json(
            """{"title":"Schedule status does not allow this","detail":"The schedule is finished. Nope."}""",
            HttpStatusCode.Conflict));
        var client = new BroadcastApiClient(Canned.Client(handler));

        var error = await Assert.ThrowsAsync<BroadcastApiException>(() => client.StopAsync(Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("The schedule is finished. Nope.", error.Message);
    }

    [Fact]
    public async Task TheProblemDocumentOfTheApiIsReadWhateverItsMediaType()
    {
        // The API answers refusals with application/problem+json (RFC 7807), which the typed JSON reader does not take.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"title":"Schedule status does not allow this","detail":"The schedule is paused."}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var client = new BroadcastApiClient(Canned.Client(handler));

        var error = await Assert.ThrowsAsync<BroadcastApiException>(() => client.StartAsync(Id));

        Assert.Equal("The schedule is paused.", error.Message);
    }

    [Fact]
    public async Task AnAnswerThatIsNotAProblemDocumentGivesTheStatusCode()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>boom</html>"),
        });
        var client = new BroadcastApiClient(Canned.Client(handler));

        var error = await Assert.ThrowsAsync<BroadcastApiException>(() => client.StartAsync(Id));

        Assert.Equal("HTTP 500", error.Message);
    }

    [Fact]
    public void TheWebCopyOfTheScheduleContractRoundTripsWithTheApi()
    {
        var api = new ApiBroadcast.ScheduleDetailDto(
            new ApiBroadcast.ScheduleDto(Id, "Spring day", "running", At, At.AddHours(24),
                new ApiBroadcast.PacingProfile(4000, 20), 7, ["gpt", "random"], 1, 0,
                [new ApiTournaments.ModelPremiumEstimateDto("gpt", 80)]),
            [new ApiBroadcast.SlotDto(Guid.Empty, 0, new ApiContracts.PlayerSpec("llm", "gpt"),
                new ApiContracts.PlayerSpec("random", "random"), At, At.AddMinutes(58), "pending", null)]);
        var apiJson = JsonSerializer.Serialize(api, Json);

        var web = JsonSerializer.Deserialize<ScheduleDetailDto>(apiJson, Json)!;

        Assert.Equal(JsonSerializer.Serialize(web, Json), apiJson);
    }

    private const string Schedule =
        """{"id":"44444444-4444-4444-4444-444444444444","name":"Spring day","status":"draft","startsAt":"2026-10-24T10:00:00Z","endsAt":"2026-10-25T10:00:00Z","pacing":{"moveDelayMs":4000,"gameGapSeconds":20},"seed":1,"models":["gpt","claude"],"slotsTotal":1,"slotsFinished":0,"estimatedPremiumRequests":[{"modelId":"gpt","estimatedPremiumRequests":80}]}""";

    private const string Detail =
        """{"schedule":{"id":"44444444-4444-4444-4444-444444444444","name":"Spring day","status":"draft","startsAt":"2026-10-24T10:00:00Z","endsAt":"2026-10-25T10:00:00Z","pacing":{"moveDelayMs":4000,"gameGapSeconds":20},"seed":1,"models":["gpt","claude"],"slotsTotal":1,"slotsFinished":0,"estimatedPremiumRequests":[{"modelId":"gpt","estimatedPremiumRequests":80}]},"slots":[{"id":"55555555-5555-5555-5555-555555555555","index":0,"white":{"kind":"llm","modelId":"gpt"},"black":{"kind":"llm","modelId":"claude"},"plannedStart":"2026-10-24T10:00:00Z","plannedEnd":"2026-10-24T10:58:00Z","state":"pending","gameId":null}]}""";
}
