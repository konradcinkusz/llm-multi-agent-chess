using System.Diagnostics;
using Chess.Api.Broadcast;
using Chess.Api.Persistence.Entities;
using Chess.Api.Ratings;
using Chess.Rating;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// The accelerated 24-hour soak (T5.6, 03 §9, ADR-011): a whole broadcast day on a fake clock, planned by the real planner
/// and played by the real runner, with a gateway outage (backoff and skips), a flaky model (a retry that plays on) and one
/// restart in the middle of the day. The fake clock makes a day of planned time cost well under a minute of real time.
/// </summary>
public class SoakTests
{
    private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(20);
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    [Fact]
    public async Task ADayWithAnOutageAFlakyModelAndARestartEndsFinishedAndItsRatingsCompute()
    {
        var watch = Stopwatch.StartNew();
        await using var h = new SoakHarness();
        var start = RunnerHarness.Start;
        var id = await h.SeedDayAsync(new PlanRequest(
            Name: "Soak day",
            Models: SoakHarness.Models,
            StartsAt: start,
            DurationHours: 24,
            Pacing: new PacingProfile(MoveDelayMs: 4000, GameGapSeconds: 20),
            Seed: SoakHarness.Seed,
            AssumedPlies: 80,
            AssumedMoveLatencyMs: 40_000));
        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(id, NoCancel));
        await h.StartRunnerAsync();

        // The restart comes after six slots have ended, with a game of the next slot in flight.
        await h.Clock.RunUntilAsync(() => h.MidGameAfterAsync(id, settled: 6, plies: 3), "a game in play after six slots");
        await h.RestartAsync();

        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");
        watch.Stop();

        var schedule = await h.ScheduleAsync(id);
        var slots = await h.SlotsAsync(id);

        // The day is 24 slots long, and the schedule ends Finished after its last slot.
        Assert.Equal(24, slots.Count);
        Assert.Equal(ScheduleStatus.Finished, schedule.Status);
        Assert.NotNull(schedule.FinishedAt);
        Assert.True(schedule.FinishedAt >= slots[^1].PlannedStart, "the schedule finished before its last slot was due");
        Assert.True(schedule.FinishedAt <= start.AddHours(24) + MaxGap, "the schedule finished after its window");

        // Every slot ends finished or skipped: none is left pending or running.
        Assert.All(slots, s => Assert.True(s.State is SlotState.Finished or SlotState.Skipped, $"slot {s.Index} is {s.State}"));

        // No two slots overlap: each planned game ends before the next one starts...
        for (var i = 1; i < slots.Count; i++)
        {
            Assert.True(slots[i - 1].PlannedEnd <= slots[i].PlannedStart, $"slot {i} starts before slot {i - 1} ends");
        }

        // ...and, in play, no game starts before the previous one has finished.
        var games = await h.StartedGamesAsync();
        Assert.All(games, g => Assert.NotNull(g.FinishedAt));
        for (var i = 1; i < games.Count; i++)
        {
            Assert.True(games[i].StartedAt!.Value >= games[i - 1].FinishedAt!.Value,
                $"game {games[i].Id} starts before game {games[i - 1].Id} has finished");
        }

        // The game in flight at the restart was cut off: it is closed as interrupted, keeps its result of *, and its slot
        // is skipped with that note.
        var cut = Assert.Single(slots, s => s.Note == "interrupted by a restart");
        Assert.Equal(SlotState.Skipped, cut.State);
        var cutGame = await h.GameAsync(cut.GameId!.Value);
        Assert.Equal("interrupted", cutGame.Termination);
        Assert.Equal("*", cutGame.Result);

        // The outage happened: a slot planned inside it was retried and then skipped, and a flaky slot had to retry and played on.
        Assert.Contains(slots, s => s.State == SlotState.Skipped
            && s.PlannedStart >= SoakHarness.OutageFrom && s.PlannedStart < SoakHarness.OutageUntil
            && s.Attempts == BroadcastRunner.MaxAttempts);
        Assert.Contains(slots, s => s.State == SlotState.Finished && s.Attempts > 1);

        // The ratings still compute from the stored games. Finished games are rated; the cut-off and the aborted ones are not.
        var rated = await RatedGameLoader.LoadAsync(h.Factory, null, NoCancel);
        var report = RatingCalculator.Compute(rated.Outcomes, rated.MoveStats, new RatingOptions());
        var finished = slots.Count(s => s.State == SlotState.Finished);
        Assert.Equal(finished, rated.Outcomes.Count);
        Assert.Equal(finished, report.Games);

        // Nothing escaped: the broadcast runner logged no error, and its background task is still healthy.
        Assert.Equal(0, h.RunnerLog.Errors);
        Assert.False(h.Runner.ExecuteTask?.IsFaulted ?? false, "the broadcast runner's background task faulted");

        Assert.True(watch.Elapsed < RealTimeLimit, $"the soak took {watch.Elapsed} of real time, over {RealTimeLimit}");
    }
}
