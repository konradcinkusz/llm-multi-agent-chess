using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Chess.Api.Tests.Games;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// The runner's timing model and its recovery from errors (03 §9.1, docs/BROADCAST.md): the next game plays after the gap
/// whatever the planned times say, the pending slots are re-timed whenever a slot leaves Running, a slot an error left
/// Running is settled, a stop ends a gap, and a command that fails to save leaves the runner alone.
/// </summary>
public class BroadcastRunnerRobustnessTests
{
    private const int MoveDelayMs = 1000;
    private const int GapSeconds = 5;

    /// <summary>A long gap, so that a runner that waited it out would be seen.</summary>
    private const int LongGapSeconds = 1800;

    private static readonly TimeSpan SlotSpacing = TimeSpan.FromMinutes(90);
    private static readonly PlayerSpec RandomPlayer = RunnerHarness.RandomPlayer;
    private static readonly PlayerSpec BrokenModel = new("llm", BrokenOrRealPlayers.BrokenModel);
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    /// <summary>Plans the slots as the planner does at its defaults: one game length plus a gap after the last, so 90 minutes apart.</summary>
    private static async Task SpaceSlotsAsync(RunnerHarness h, Guid scheduleId)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        foreach (var slot in await db.BroadcastSlots.Where(s => s.ScheduleId == scheduleId).ToListAsync())
        {
            slot.PlannedStart = RunnerHarness.Start + (slot.Index * SlotSpacing);
            slot.PlannedEnd = slot.PlannedStart + TimeSpan.FromHours(1);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<DateTimeOffset> EndsAtAsync(RunnerHarness h, Guid scheduleId)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        return (await db.BroadcastSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId)).EndsAt;
    }

    private static async Task<Guid> SeedGameAsync(RunnerHarness h, string? termination, DateTimeOffset? finishedAt)
    {
        var id = Guid.NewGuid();
        await using var db = await h.Factory.CreateDbContextAsync();
        db.Games.Add(new GameEntity
        {
            Id = id,
            WhiteKind = RandomPlayer.Kind,
            WhiteModel = RandomPlayer.ModelId,
            BlackKind = RandomPlayer.Kind,
            BlackModel = RandomPlayer.ModelId,
            Rated = true,
            Result = finishedAt is null || termination == GameTermination.Aborted.Termination ? "*" : "1-0",
            Termination = termination,
            StartFen = GameHarness.StartFen,
            CurrentFen = GameHarness.StartFen,
            CreatedAt = RunnerHarness.Start,
            StartedAt = RunnerHarness.Start,
            FinishedAt = finishedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Makes the first slot an orphan: the schedule runs, the slot is Running with the given game, and no runner is playing it.
    /// The runner is started first on a Draft schedule and given time to pass its start-up recovery, so that the orphan appears
    /// while it runs, as after a database error, and not at a restart.
    /// </summary>
    private static async Task<(Guid GameId, DateTimeOffset At)> OrphanFirstSlotAsync(
        RunnerHarness h, Guid scheduleId, string? termination, bool finished, int attempts)
    {
        await h.StartRunnerAsync();
        await h.Clock.RunForAsync(TimeSpan.FromMilliseconds(300));
        var at = h.Clock.GetUtcNow();
        var gameId = await SeedGameAsync(h, termination, finished ? at : null);
        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            var first = await db.BroadcastSlots.Where(s => s.ScheduleId == scheduleId).OrderBy(s => s.Index).FirstAsync();
            first.State = SlotState.Running;
            first.GameId = gameId;
            first.Attempts = attempts;
            await db.SaveChangesAsync();
        }

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(scheduleId, NoCancel));
        return (gameId, at);
    }

    [Fact]
    public async Task A_skipped_slot_does_not_make_the_next_game_wait_for_its_planned_start()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (BrokenModel, RandomPlayer), (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        await SpaceSlotsAsync(h, id);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");

        var slots = await h.SlotsAsync(id);
        var games = await h.GamesAsync();
        var failed = games.Where(g => g.WhiteModel == BrokenOrRealPlayers.BrokenModel).ToList();
        var second = await h.GameAsync(slots[1].GameId!.Value);
        var third = await h.GameAsync(slots[2].GameId!.Value);

        // Slot 1 was planned 90 minutes in; it starts one gap after the failed slot was skipped, a couple of minutes in.
        Assert.Equal(SlotState.Skipped, slots[0].State);
        Assert.Equal(TimeSpan.FromSeconds(GapSeconds), second.CreatedAt - failed[^1].FinishedAt!.Value);
        Assert.True(second.CreatedAt < RunnerHarness.Start + TimeSpan.FromMinutes(5), $"slot 1 started at {second.CreatedAt}");
        Assert.Equal(TimeSpan.FromSeconds(GapSeconds), third.CreatedAt - second.FinishedAt!.Value);

        // The plan follows what happened: the pending slots were moved to their real starts, the ended ones end when they ended.
        Assert.Equal(second.CreatedAt, slots[1].PlannedStart);
        Assert.Equal(second.FinishedAt, slots[1].PlannedEnd);
        Assert.Equal(third.CreatedAt, slots[2].PlannedStart);
        Assert.Equal(third.FinishedAt, slots[2].PlannedEnd);
        Assert.Equal(third.FinishedAt, await EndsAtAsync(h, id));
    }

    [Fact]
    public async Task The_pending_slots_and_the_end_are_re_timed_while_the_day_plays()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        await SpaceSlotsAsync(h, id);
        await h.StartRunnerAsync();

        // The first game is over and the runner holds the gap: the two pending slots are planned from its end, back to back.
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(id))[0].State == SlotState.Finished, "the first slot to finish");
        var slots = await h.SlotsAsync(id);
        var first = await h.GameAsync(slots[0].GameId!.Value);
        var gap = TimeSpan.FromSeconds(GapSeconds);
        Assert.Equal(first.FinishedAt, slots[0].PlannedEnd);
        Assert.Equal(first.FinishedAt + gap, slots[1].PlannedStart);
        Assert.Equal(slots[1].PlannedStart + TimeSpan.FromHours(1) + gap, slots[2].PlannedStart);
        Assert.Equal(slots[2].PlannedEnd, await EndsAtAsync(h, id));
        Assert.True(await EndsAtAsync(h, id) < RunnerHarness.Start.AddHours(24), "the end moved from the original window");
    }

    [Fact]
    public async Task After_a_restart_the_next_slot_plays_at_once_and_the_plan_is_moved_to_now()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        await SpaceSlotsAsync(h, id);
        var interrupted = await SeedGameAsync(h, null, null);
        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            var first = await db.BroadcastSlots.Where(s => s.ScheduleId == id).OrderBy(s => s.Index).FirstAsync();
            first.State = SlotState.Running;
            first.GameId = interrupted;
            first.Attempts = 1;
            await db.SaveChangesAsync();
        }

        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(id))[1].GameId is not null, "the second slot to start");

        var slots = await h.SlotsAsync(id);
        var game = await h.GameAsync(slots[1].GameId!.Value);
        Assert.Equal(SlotState.Skipped, slots[0].State);
        Assert.Equal(RunnerHarness.Start, slots[0].PlannedEnd);
        Assert.Equal(RunnerHarness.Start, slots[1].PlannedStart);
        Assert.Equal(RunnerHarness.Start, game.CreatedAt);
    }

    [Fact]
    public async Task A_slot_left_running_with_a_finished_game_is_finished_and_the_stage_moves_on()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        var (finished, at) = await OrphanFirstSlotAsync(h, id, "checkmate", finished: true, attempts: 1);

        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(id))[1].GameId is not null, "the second slot to start");

        var slots = await h.SlotsAsync(id);
        Assert.Equal(SlotState.Finished, slots[0].State);
        Assert.Null(slots[0].Note);
        Assert.Equal(finished, slots[0].GameId);
        var next = await h.GameAsync(slots[1].GameId!.Value);
        Assert.Equal(TimeSpan.FromSeconds(GapSeconds), next.CreatedAt - at);
        Assert.Equal(at, slots[0].PlannedEnd);
        Assert.NotEqual(slots[0].Id, (await h.Controller.NowAsync(NoCancel)).Current?.Id);
    }

    [Fact]
    public async Task A_slot_left_running_with_an_unfinished_game_is_skipped_and_the_game_is_closed()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        var (dead, _) = await OrphanFirstSlotAsync(h, id, null, finished: false, attempts: 1);

        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(id))[1].GameId is not null, "the second slot to start");

        var slots = await h.SlotsAsync(id);
        Assert.Equal(SlotState.Skipped, slots[0].State);
        Assert.Equal("interrupted by an error", slots[0].Note);
        var game = await h.GameAsync(dead);
        Assert.Equal("interrupted", game.Termination);
        Assert.NotNull(game.FinishedAt);
        Assert.Equal("*", game.Result);
    }

    [Fact]
    public async Task A_slot_left_running_with_an_aborted_game_is_tried_again()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));
        var (aborted, _) = await OrphanFirstSlotAsync(h, id, GameTermination.Aborted.Termination, finished: true, attempts: 1);

        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");

        var slot = (await h.SlotsAsync(id)).Single();
        Assert.True(slot.State == SlotState.Finished, $"slot {slot.State}, note {slot.Note}, attempts {slot.Attempts}");
        Assert.NotEqual(aborted, slot.GameId);
        Assert.Equal(2, slot.Attempts);
    }

    [Fact]
    public async Task A_stop_in_the_gap_ends_it_so_a_new_day_plays_at_once()
    {
        await using var h = new RunnerHarness();
        var first = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, LongGapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        var second = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, LongGapSeconds, (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(first))[0].State == SlotState.Finished, "the first slot to finish");
        var endedAt = (await h.GameAsync((await h.SlotsAsync(first))[0].GameId!.Value)).FinishedAt!.Value;

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StopAsync(first, NoCancel));
        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(second, NoCancel));
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(second))[0].GameId is not null, "the new day's game to start");

        var game = await h.GameAsync((await h.SlotsAsync(second))[0].GameId!.Value);
        Assert.True(game.CreatedAt - endedAt < TimeSpan.FromMinutes(1), $"the new day waited {game.CreatedAt - endedAt}");
    }

    [Fact]
    public async Task A_stop_in_the_gap_after_a_request_ends_it_so_a_new_day_plays_at_once()
    {
        await using var h = new RunnerHarness(Priced("custom-a"), Priced("custom-b"));
        var first = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, LongGapSeconds);
        var second = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, LongGapSeconds, (RandomPlayer, RandomPlayer));
        var requestId = await QueueRequestAsync(h);
        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => (await RequestStatusAsync(h, requestId)) == RequestStatus.Played, "the request to be played");
        var endedAt = (await h.GamesAsync()).Single().FinishedAt!.Value;

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StopAsync(first, NoCancel));
        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(second, NoCancel));
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(second))[0].GameId is not null, "the new day's game to start");

        var game = await h.GameAsync((await h.SlotsAsync(second))[0].GameId!.Value);
        Assert.True(game.CreatedAt - endedAt < TimeSpan.FromMinutes(1), $"the new day waited {game.CreatedAt - endedAt}");
    }

    [Fact]
    public async Task A_command_that_fails_to_save_leaves_the_pause_flag_and_the_game_alone()
    {
        var interceptor = new FailOnSave();
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        var signals = new BroadcastRunnerSignals();
        var controller = new BroadcastRunnerController(factory, signals, new RunnerClock(RunnerHarness.Start));
        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BroadcastSchedules.Add(new BroadcastScheduleEntity
            {
                Id = id,
                Name = "Test day",
                Status = ScheduleStatus.Running,
                StartsAt = RunnerHarness.Start,
                EndsAt = RunnerHarness.Start.AddHours(1),
                MoveDelayMs = MoveDelayMs,
                GameGapSeconds = GapSeconds,
                Seed = 1,
                Models = [],
                CreatedAt = RunnerHarness.Start,
            });
            await db.SaveChangesAsync();
        }

        interceptor.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.PauseAsync(id, NoCancel));
        Assert.False(signals.Paused, "the pause flag was set although the save failed");

        using var game = new CancellationTokenSource();
        signals.BeginGame(game);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StopAsync(id, NoCancel));
        Assert.False(game.IsCancellationRequested, "the game was aborted although the save failed");

        interceptor.Fail = false;
        Assert.Equal(BroadcastCommandResult.Accepted, await controller.PauseAsync(id, NoCancel));
        Assert.True(signals.Paused);
        interceptor.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ResumeAsync(id, NoCancel));
        Assert.True(signals.Paused, "the pause flag was cleared although the save failed");
    }

    private static CustomModelOptions Priced(string id) => new()
    {
        Id = id,
        ProviderType = "openai",
        BaseUrl = "https://models.invalid/v1",
        Model = id,
        UsdPerMillionInput = 1,
        UsdPerMillionOutput = 2,
    };

    private static async Task<Guid> QueueRequestAsync(RunnerHarness h)
    {
        var id = Guid.NewGuid();
        await using var db = await h.Factory.CreateDbContextAsync();
        db.GameRequests.Add(new GameRequestEntity
        {
            Id = id,
            Source = "mock",
            ExternalId = $"mock:{id:N}",
            PayerId = "viewer",
            PayerDisplay = "Viewer",
            White = "custom-a",
            Black = "custom-b",
            PriceMicros = Money.FromUnits(1000),
            Status = RequestStatus.Queued,
            ReceivedAt = RunnerHarness.Start.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<string> RequestStatusAsync(RunnerHarness h, Guid id)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        return (await db.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id)).Status;
    }

    /// <summary>Fails the next saves while <see cref="Fail"/> is set, as a database error past the retries does.</summary>
    private sealed class FailOnSave : SaveChangesInterceptor
    {
        public volatile bool Fail;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("the database is down") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
