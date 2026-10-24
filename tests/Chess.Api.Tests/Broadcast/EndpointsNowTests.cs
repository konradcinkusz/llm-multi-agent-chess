using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// <c>GET /api/broadcast/now</c> (03 §9.2, §9.4): what the OBS page shows. Each test starts from its own empty store, so
/// the only active schedule is the one the test seeds.
/// </summary>
public class EndpointsNowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");
    private static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Now_is_idle_when_no_schedule_is_active()
    {
        using var h = new BroadcastHarness();
        await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending, SlotState.Pending);
        await SeedAsync(h, ScheduleStatus.Finished, SlotState.Finished);

        var now = await NowAsync(h);

        Assert.Equal("idle", now.Status);
        Assert.Null(now.Current);
        Assert.Empty(now.Next);
        Assert.Null(now.Notice);
        Assert.Null(now.EndsAt);
    }

    [Fact]
    public async Task Now_while_running_shows_the_playing_slot_and_the_next_three()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Running, SlotState.Finished, SlotState.Running, SlotState.Pending,
            SlotState.Pending, SlotState.Pending, SlotState.Pending);

        var now = await NowAsync(h);

        Assert.Equal("running", now.Status);
        Assert.NotNull(now.Current);
        Assert.Equal(1, now.Current.Index);
        Assert.Equal(SlotState.Running, now.Current.State);
        Assert.Equal(new[] { 2, 3, 4 }, now.Next.Select(s => s.Index));
        Assert.All(now.Next, slot => Assert.Equal(SlotState.Pending, slot.State));
        Assert.Equal(Start.AddHours(24), now.EndsAt);
        Assert.Equal(id, await ScheduleIdOfAsync(h, now.Current.Id));
    }

    [Fact]
    public async Task Now_between_games_has_no_current_slot_but_still_lists_what_is_next()
    {
        using var h = new BroadcastHarness();
        await SeedAsync(h, ScheduleStatus.Running, SlotState.Finished, SlotState.Finished, SlotState.Pending);

        var now = await NowAsync(h);

        Assert.Equal("running", now.Status);
        Assert.Null(now.Current);
        Assert.Equal(new[] { 2 }, now.Next.Select(s => s.Index));
    }

    [Fact]
    public async Task Now_while_paused_reports_paused_with_the_slot_that_was_playing()
    {
        using var h = new BroadcastHarness();
        await SeedAsync(h, ScheduleStatus.Paused, SlotState.Finished, SlotState.Running, SlotState.Pending);

        var now = await NowAsync(h);

        Assert.Equal("paused", now.Status);
        Assert.Equal(1, now.Current?.Index);
        Assert.Equal(new[] { 2 }, now.Next.Select(s => s.Index));
    }

    [Fact]
    public async Task Now_lists_no_more_than_the_next_three_pending_slots()
    {
        using var h = new BroadcastHarness();
        await SeedAsync(h, ScheduleStatus.Running, Enumerable.Repeat(SlotState.Pending, 10).ToArray());

        var now = await NowAsync(h);

        Assert.Equal(BroadcastEndpoints.NextCount, now.Next.Count);
        Assert.Equal(new[] { 0, 1, 2 }, now.Next.Select(s => s.Index));
    }

    private static async Task<NowNextDto> NowAsync(BroadcastHarness h)
    {
        var response = await h.Client.GetAsync("/api/broadcast/now");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NowNextDto>(Json))!;
    }

    private static async Task<Guid?> ScheduleIdOfAsync(BroadcastHarness h, Guid slotId)
    {
        using var db = h.App.NewContext();
        return (await db.BroadcastSlots.FindAsync(slotId))?.ScheduleId;
    }

    /// <summary>Stores a schedule in the given status with one slot per state, alternating colours, back to back.</summary>
    private static async Task<Guid> SeedAsync(BroadcastHarness h, string status, params string[] slotStates)
    {
        var id = Guid.NewGuid();
        using var db = h.App.NewContext();
        db.BroadcastSchedules.Add(new BroadcastScheduleEntity
        {
            Id = id,
            Name = "Seeded",
            Status = status,
            StartsAt = Start,
            EndsAt = Start.AddHours(24),
            MoveDelayMs = 4000,
            GameGapSeconds = 20,
            Seed = 7,
            Models = ["fake-a", "fake-b"],
            CreatedAt = Start.AddDays(-1),
        });
        for (var index = 0; index < slotStates.Length; index++)
        {
            var start = Start.AddMinutes(index * (SlotLength.TotalMinutes + 10));
            var white = index % 2 == 0 ? FakeA : FakeB;
            var black = index % 2 == 0 ? FakeB : FakeA;
            db.BroadcastSlots.Add(new BroadcastSlotEntity
            {
                Id = Guid.NewGuid(),
                ScheduleId = id,
                Index = index,
                WhiteKind = white.Kind,
                WhiteModel = white.ModelId,
                BlackKind = black.Kind,
                BlackModel = black.ModelId,
                PlannedStart = start,
                PlannedEnd = start + SlotLength,
                State = slotStates[index],
            });
        }
        await db.SaveChangesAsync();
        return id;
    }
}
