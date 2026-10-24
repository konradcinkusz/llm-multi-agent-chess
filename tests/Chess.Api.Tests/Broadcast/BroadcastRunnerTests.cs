using Chess.Api.Contracts;
using Chess.Api.Broadcast;
using Chess.Api.Persistence.Entities;
using Chess.Api.Tests.Games;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

public class BroadcastRunnerTests
{
    private const int MoveDelayMs = 1000;
    private const int GapSeconds = 5;

    private static readonly PlayerSpec RandomPlayer = RunnerHarness.RandomPlayer;
    private static readonly PlayerSpec BrokenModel = new("llm", BrokenOrRealPlayers.BrokenModel);
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    [Fact]
    public async Task SlotsRunInOrderOneGameAtATimeAndEachMoveIsPaced()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");

        var slots = await h.SlotsAsync(id);
        Assert.All(slots, s => Assert.Equal(SlotState.Finished, s.State));
        var games = await h.GamesAsync();
        Assert.Equal(3, games.Count);
        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            Assert.Equal(slots[i].GameId, game.Id);
            Assert.True(game.Rated);
            Assert.NotEqual("*", game.Result);
            Assert.NotNull(game.StartedAt);
            Assert.NotNull(game.FinishedAt);

            // One pause after every move, so the game lasts exactly its plies times the move delay.
            Assert.Equal(TimeSpan.FromMilliseconds(game.PlyCount * MoveDelayMs), game.FinishedAt!.Value - game.StartedAt!.Value);

            // The next game is created only once the last one has finished, after the game gap.
            if (i > 0)
            {
                Assert.Equal(TimeSpan.FromSeconds(GapSeconds), game.CreatedAt - games[i - 1].FinishedAt!.Value);
            }
        }

        var finished = await h.ScheduleAsync(id);
        Assert.NotNull(finished.FinishedAt);
    }

    [Fact]
    public async Task PauseHoldsTheNextMoveUntilResume()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => await PlyReachedAsync(h, id, 3), "three plies");

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.PauseAsync(id, NoCancel));

        // A move that was already in flight may finish; after that nothing moves while the broadcast is paused.
        await h.Clock.RunForAsync(TimeSpan.FromMilliseconds(500));
        var gameId = (await h.SlotsAsync(id)).Single().GameId!.Value;
        var held = await h.PlyCountAsync(gameId);
        await h.Clock.RunForAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(held, await h.PlyCountAsync(gameId));
        Assert.Equal(ScheduleStatus.Paused, (await h.ScheduleAsync(id)).Status);
        Assert.Equal(SlotState.Running, (await h.SlotsAsync(id)).Single().State);

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.ResumeAsync(id, NoCancel));
        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");
        Assert.True(await h.PlyCountAsync(gameId) > held);
    }

    [Fact]
    public async Task AfterARestartTheInterruptedSlotIsSkippedAndThePlayResumesAtTheNext()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        var interruptedGame = Guid.NewGuid();
        await SeedUnfinishedGameAsync(h, interruptedGame);
        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            var first = await db.BroadcastSlots.Where(s => s.ScheduleId == id).OrderBy(s => s.Index).FirstAsync();
            first.State = SlotState.Running;
            first.GameId = interruptedGame;
            first.Attempts = 1;
            await db.SaveChangesAsync();
        }

        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished, "the schedule to finish");

        var slots = await h.SlotsAsync(id);
        Assert.Equal(SlotState.Skipped, slots[0].State);
        Assert.Equal("interrupted by a restart", slots[0].Note);
        Assert.Equal(SlotState.Finished, slots[1].State);
        Assert.NotEqual(interruptedGame, slots[1].GameId);
    }

    [Fact]
    public async Task AGatewayThatAlwaysFailsGivesThreeAttemptsThenSkipsTheSlotAndTheNextSlotRuns()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (BrokenModel, RandomPlayer), (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();

        var sawNotice = false;
        await h.Clock.RunUntilAsync(async () =>
        {
            sawNotice |= (await h.Controller.NowAsync(NoCancel)).Notice is not null;
            return (await h.ScheduleAsync(id)).Status == ScheduleStatus.Finished;
        }, "the schedule to finish");

        Assert.True(sawNotice, "the technical-break notice was shown while backing off");
        var slots = await h.SlotsAsync(id);
        Assert.Equal(SlotState.Skipped, slots[0].State);
        Assert.Equal(BroadcastRunner.MaxAttempts, slots[0].Attempts);
        Assert.Equal("skipped after 3 attempts: the model gave no reply", slots[0].Note);
        Assert.Equal(SlotState.Finished, slots[1].State);

        // Each failed game is aborted, so it is left out of the rating; the retries wait 30 s, then 60 s.
        var failed = (await h.GamesAsync()).Where(g => g.WhiteModel == BrokenOrRealPlayers.BrokenModel).ToList();
        Assert.Equal(BroadcastRunner.MaxAttempts, failed.Count);
        Assert.All(failed, g =>
        {
            Assert.Equal("aborted", g.Termination);
            Assert.Equal("*", g.Result);
        });
        Assert.Equal(TimeSpan.FromSeconds(30), failed[1].CreatedAt - failed[0].FinishedAt!.Value);
        Assert.Equal(TimeSpan.FromSeconds(60), failed[2].CreatedAt - failed[1].FinishedAt!.Value);

        var next = (await h.GamesAsync()).Single(g => g.WhiteModel == RandomPlayer.ModelId && g.CreatedAt > failed[2].FinishedAt!.Value);
        Assert.Equal(TimeSpan.FromSeconds(GapSeconds), next.CreatedAt - failed[2].FinishedAt!.Value);
    }

    [Fact]
    public async Task StopAbortsTheGameInPlaySkipsItsSlotAndCancelsTheSchedule()
    {
        await using var h = new RunnerHarness();
        var id = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => await PlyReachedAsync(h, id, 2), "two plies");
        var gameId = (await h.SlotsAsync(id)).Single(s => s.State == SlotState.Running).GameId!.Value;

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StopAsync(id, NoCancel));
        await h.Clock.RunUntilAsync(async () => (await h.SlotsAsync(id)).First().State == SlotState.Skipped, "the slot to be skipped");

        var slot = (await h.SlotsAsync(id)).First();
        Assert.Equal("stopped by the operator", slot.Note);
        var game = await h.GameAsync(gameId);
        Assert.Equal("aborted", game.Termination);
        Assert.Equal("*", game.Result);
        Assert.Equal(ScheduleStatus.Cancelled, (await h.ScheduleAsync(id)).Status);

        await h.Clock.RunForAsync(TimeSpan.FromSeconds(2));
        Assert.Single(await h.GamesAsync());
    }

    [Fact]
    public async Task NowShowsTheStatusTheGameInPlayAndTheUpNextSlots()
    {
        await using var h = new RunnerHarness();
        var first = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds,
            (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer), (RandomPlayer, RandomPlayer));
        var second = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));

        var idle = await h.Controller.NowAsync(NoCancel);
        Assert.Equal(NowNextStatus.Idle, idle.Status);
        Assert.Null(idle.Current);
        Assert.Empty(idle.Next);

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(first, NoCancel));
        var running = await h.Controller.NowAsync(NoCancel);
        Assert.Equal(ScheduleStatus.Running, running.Status);
        Assert.Null(running.Current);
        Assert.Equal(3, running.Next.Count);
        Assert.Equal(new[] { 0, 1, 2 }, running.Next.Select(s => s.Index));
        Assert.Equal(RunnerHarness.Start.AddHours(24), running.EndsAt);
        Assert.Null(running.Notice);

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.PauseAsync(first, NoCancel));
        Assert.Equal(ScheduleStatus.Paused, (await h.Controller.NowAsync(NoCancel)).Status);

        // Only one channel: the second schedule cannot start while the first one is playing or paused.
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.StartAsync(second, NoCancel));
    }

    [Fact]
    public async Task ControllerAnswersNotFoundAndRefusesIllegalTransitions()
    {
        await using var h = new RunnerHarness();
        var draft = await h.SeedScheduleAsync(ScheduleStatus.Draft, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));
        var unknown = Guid.NewGuid();

        Assert.Equal(BroadcastCommandResult.NotFound, await h.Controller.StartAsync(unknown, NoCancel));
        Assert.Equal(BroadcastCommandResult.NotFound, await h.Controller.PauseAsync(unknown, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.PauseAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.ResumeAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.StopAsync(draft, NoCancel));

        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StartAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.StartAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.ResumeAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Accepted, await h.Controller.StopAsync(draft, NoCancel));
        Assert.Equal(BroadcastCommandResult.Conflict, await h.Controller.StopAsync(draft, NoCancel));
        Assert.Equal(NowNextStatus.Idle, (await h.Controller.NowAsync(NoCancel)).Status);
    }




    private static async Task<bool> PlyReachedAsync(RunnerHarness h, Guid scheduleId, int plies)
    {
        var slot = (await h.SlotsAsync(scheduleId)).SingleOrDefault(s => s.State == SlotState.Running && s.GameId is not null);
        return slot is not null && await h.PlyCountAsync(slot.GameId!.Value) >= plies;
    }

    private static async Task SeedUnfinishedGameAsync(RunnerHarness h, Guid gameId)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        db.Games.Add(new GameEntity
        {
            Id = gameId,
            WhiteKind = RandomPlayer.Kind,
            WhiteModel = RandomPlayer.ModelId,
            BlackKind = RandomPlayer.Kind,
            BlackModel = RandomPlayer.ModelId,
            Rated = true,
            Result = "*",
            StartFen = GameHarness.StartFen,
            CurrentFen = GameHarness.StartFen,
            CreatedAt = RunnerHarness.Start,
            StartedAt = RunnerHarness.Start,
        });
        await db.SaveChangesAsync();
    }
}
