using Chess.Analysis;
using Chess.Api.Games;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>How a game is closed when something goes wrong at its end: a save that fails, a budget, an engine that throws.</summary>
public class GameFinishingTests
{
    private static readonly PlayerDescriptor LlmPlayer = new("llm", "fake-a");

    /// <summary>The knights go out and back twice: the start position is on the board a third time after ply 8.</summary>
    private static readonly string[] Shuffle = ["Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8"];

    private static ScriptedPlayerFactory Shuffling(string reply = "reply") =>
        new((descriptor, _) => new ScriptedPlayer(descriptor, (request, _) =>
        {
            var san = Shuffle[request.Ply - 1];
            var attempt = new MoveAttempt(1, "prompt", reply, san, true, TimeSpan.FromMilliseconds(1), 1, 1, null);
            return Task.FromResult(new PlayerMove(san, [attempt], Forfeit: false));
        }));

    private sealed class SpentAfter(int moves) : IGameBudget
    {
        private int _asked;

        public Task<bool> IsExhaustedAsync(Guid gameId, CancellationToken ct) => Task.FromResult(++_asked >= moves);
    }

    private sealed class ThrowingAnalyzer : IMoveQualityAnalyzer
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MoveQuality>>([]);

        public Task<int?> EvaluateAsync(string fen, CancellationToken ct) => throw new InvalidOperationException("The engine crashed.");
    }

    /// <summary>Refuses to save a move attempt whose reply is the poison, as PostgreSQL refuses a text with U+0000.</summary>
    private sealed class PoisonInterceptor : SaveChangesInterceptor
    {
        public const string Poison = "POISON";

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<MoveAttemptEntity>()
                .Any(e => e.State == EntityState.Added && e.Entity.RawReply == Poison))
            {
                throw new InvalidOperationException("The database refused the reply.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_ply_that_cannot_be_saved_does_not_stop_the_game_from_being_closed()
    {
        var published = new List<IGameEvent>();
        await using var harness = new GameHarness(Shuffling(PoisonInterceptor.Poison),
            onPublish: published.Add, configureDb: o => o.AddInterceptors(new PoisonInterceptor()));
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, LlmPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        // The row is closed in the database, not only announced: the tournament's slot is freed by it.
        var game = await harness.LoadGameAsync(gameId);
        Assert.NotNull(game.FinishedAt);
        Assert.Equal("aborted", game.Termination);
        Assert.Equal(0, game.PlyCount);
        Assert.Equal("aborted", Assert.IsType<GameFinished>(events[^1]).Termination);
        Assert.Empty(await harness.LoadMovesAsync(gameId));
        Assert.Contains("[Result \"*\"]", game.Pgn);
    }

    [Fact]
    public async Task A_budget_spent_by_the_move_that_repeats_the_position_for_the_third_time_ends_as_threefold()
    {
        await using var harness = new GameHarness(Shuffling());
        var gameId = await harness.SeedGameAsync(LlmPlayer, LlmPlayer);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var end = await harness.Runner.PlayToEndAsync(gameId, new GameRunHooks(Budget: new SpentAfter(8)), CancellationToken.None, stop.Token);

        Assert.Equal("threefold", end!.Termination);
        Assert.Equal("1/2-1/2", end.Result);
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal("threefold", game.Termination);
        Assert.Equal(8, game.PlyCount);
    }

    [Fact]
    public async Task A_budget_spent_before_any_repetition_is_still_adjudicated_as_budget()
    {
        await using var harness = new GameHarness(Shuffling());
        var gameId = await harness.SeedGameAsync(LlmPlayer, LlmPlayer);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var end = await harness.Runner.PlayToEndAsync(gameId, new GameRunHooks(Budget: new SpentAfter(5)), CancellationToken.None, stop.Token);

        Assert.Equal(GameTermination.BudgetReached, end!.Termination);
    }

    [Fact]
    public async Task An_engine_that_throws_at_MaxPlies_falls_back_to_the_material_rule()
    {
        await using var harness = new GameHarness(Shuffling(), new ThrowingAnalyzer(), maxPlies: 4);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, LlmPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal(GameTermination.Adjudicated, finished.Termination);
        Assert.Equal("1/2-1/2", finished.Result);
        Assert.Equal(4, (await harness.LoadGameAsync(gameId)).PlyCount);
    }

    [Fact]
    public async Task An_engine_that_throws_at_a_budget_falls_back_to_the_material_rule()
    {
        await using var harness = new GameHarness(Shuffling(), new ThrowingAnalyzer());
        var gameId = await harness.SeedGameAsync(LlmPlayer, LlmPlayer);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var end = await harness.Runner.PlayToEndAsync(gameId, new GameRunHooks(Budget: new SpentAfter(3)), CancellationToken.None, stop.Token);

        Assert.Equal(GameTermination.BudgetReached, end!.Termination);
        Assert.Equal("1/2-1/2", end.Result);
    }
}
