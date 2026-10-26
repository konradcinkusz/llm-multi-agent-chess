using Chess.Api.Catalog;
using Chess.Api.Games;
using Chess.Api.Players;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>The budget hook of an awaitable game (03 §15.2): it ends the game as <c>budget</c>, adjudicated like <c>MaxPlies</c>.</summary>
public class GameBudgetTests
{
    private static readonly PlayerDescriptor RandomPlayer = new(ModelCatalog.RandomModelId, ModelCatalog.RandomModelId);

    /// <summary>A budget that is spent after the given number of moves, counting every ask.</summary>
    private sealed class SpentAfter(int moves) : IGameBudget
    {
        private int _asked;

        public int Asked => _asked;

        public Task<bool> IsExhaustedAsync(Guid gameId, CancellationToken ct) => Task.FromResult(++_asked >= moves);
    }

    [Fact]
    public async Task A_spent_budget_ends_the_game_as_budget_after_the_move_that_spent_it()
    {
        await using var h = new GameHarness(GameHarness.RealPlayers());
        var gameId = await h.SeedGameAsync(RandomPlayer, RandomPlayer);
        var budget = new SpentAfter(3);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var end = await h.Runner.PlayToEndAsync(gameId, new GameRunHooks(Budget: budget), CancellationToken.None, stop.Token);

        Assert.Equal(GameTermination.BudgetReached, end!.Termination);
        Assert.Equal(3, budget.Asked);
        await using var db = await h.Factory.CreateDbContextAsync();
        var game = await db.Games.SingleAsync(g => g.Id == gameId);
        Assert.Equal(3, game.PlyCount);
        Assert.Equal(GameTermination.BudgetReached, game.Termination);
        Assert.NotEqual("*", game.Result);
        Assert.NotNull(game.FinishedAt);
    }

    [Fact]
    public async Task A_budget_that_is_never_spent_leaves_the_game_to_its_own_end()
    {
        await using var h = new GameHarness(GameHarness.RealPlayers(), maxPlies: 6);
        var gameId = await h.SeedGameAsync(RandomPlayer, RandomPlayer);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var end = await h.Runner.PlayToEndAsync(gameId, new GameRunHooks(Budget: new SpentAfter(int.MaxValue)),
            CancellationToken.None, stop.Token);

        Assert.NotEqual(GameTermination.BudgetReached, end!.Termination);
        await using var db = await h.Factory.CreateDbContextAsync();
        Assert.True((await db.Games.SingleAsync(g => g.Id == gameId)).PlyCount <= 6);
    }

    [Fact]
    public void The_budget_termination_takes_the_material_verdict_under_its_own_word()
    {
        var board = new ChessBoard();

        var budget = GameTermination.Adjudicate(board, null, GameTermination.BudgetReached);
        var plies = GameTermination.Adjudicate(board, null);

        Assert.Equal("budget", budget.Termination);
        Assert.Equal("adjudicated", plies.Termination);
        Assert.Equal(plies.Result, budget.Result);
    }
}
