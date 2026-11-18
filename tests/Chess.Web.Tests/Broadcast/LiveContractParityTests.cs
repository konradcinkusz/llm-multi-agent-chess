using System.Text.Json;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;
using ApiBroadcast = Chess.Api.Broadcast;
using ApiDto = Chess.Api.Contracts;

namespace Chess.Web.Tests.Broadcast;

/// <summary>
/// The OBS page reads the broadcast with its own small records (<see cref="LiveNowNext"/>, <see cref="LiveSlot"/>,
/// <see cref="LiveScheduleSummary"/>). They must keep reading what the API sends (03 §9.2): the API's own DTOs are
/// serialised as the API does and read back through the page's client.
/// </summary>
public class LiveContractParityTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset At = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheNowAndNextTheApiSendsIsReadIntoTheLiveRecords()
    {
        var gameId = Guid.NewGuid();
        var current = Slot(16, "gpt@high", "claude", "running", gameId);
        var next = Slot(17, "claude", "random", "pending", null);
        var api = new ApiBroadcast.NowNextDto("running", current, [next], "Copilot is rate limited.", At.AddHours(10));

        var read = await ClientFor(JsonSerializer.Serialize(api, ApiJson)).GetNowAsync();

        Assert.Equal("running", read.Status);
        Assert.Equal("Copilot is rate limited.", read.Notice);
        Assert.Equal(At.AddHours(10), read.EndsAt);
        AssertSlot(current, read.Current!);
        AssertSlot(next, Assert.Single(read.Next));
    }

    [Fact]
    public async Task AnIdleNowWithoutASlotIsReadToo()
    {
        var api = new ApiBroadcast.NowNextDto("idle", null, [], null, null);

        var read = await ClientFor(JsonSerializer.Serialize(api, ApiJson)).GetNowAsync();

        Assert.Equal("idle", read.Status);
        Assert.Null(read.Current);
        Assert.Empty(read.Next);
        Assert.Null(read.Notice);
        Assert.Null(read.EndsAt);
    }

    [Fact]
    public async Task TheSchedulesTheApiSendsAreReadWithTheirPacing()
    {
        var schedule = new ApiBroadcast.ScheduleDto(Guid.NewGuid(), "Day one", "running", At, At.AddHours(24),
            new ApiBroadcast.PacingProfile(4000, 1800), 7, ["gpt", "claude"], 48, 16, []);

        var read = await ClientFor(JsonSerializer.Serialize(new[] { schedule }, ApiJson)).GetSchedulesAsync();

        var summary = Assert.Single(read);
        Assert.Equal(schedule.Id, summary.Id);
        Assert.Equal("running", summary.Status);
        Assert.Equal(48, summary.SlotsTotal);
        Assert.Equal(16, summary.SlotsFinished);
        Assert.Equal(4000, summary.Pacing!.MoveDelayMs);
        Assert.Equal(1800, summary.Pacing.GameGapSeconds);
    }

    private static ApiBroadcast.SlotDto Slot(int index, string white, string black, string state, Guid? gameId) =>
        new(Guid.NewGuid(), index, new ApiDto.PlayerSpec("llm", white), new ApiDto.PlayerSpec("llm", black), At,
            At.AddMinutes(30), state, gameId);

    private static void AssertSlot(ApiBroadcast.SlotDto api, LiveSlot live)
    {
        Assert.Equal(api.Id, live.Id);
        Assert.Equal(api.Index, live.Index);
        Assert.Equal(api.White.ModelId, live.White.ModelId);
        Assert.Equal(api.White.Kind, live.White.Kind);
        Assert.Equal(api.Black.ModelId, live.Black.ModelId);
        Assert.Equal(api.PlannedStart, live.PlannedStart);
        Assert.Equal(api.PlannedEnd, live.PlannedEnd);
        Assert.Equal(api.State, live.State);
        Assert.Equal(api.GameId, live.GameId);
    }

    private static LiveBroadcastClient ClientFor(string json) =>
        new(Canned.Client(new FakeHandler(_ => Canned.Json(json))));
}
