using Chess.Api.Broadcast;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>
/// After a restart the real runner queues again only the games nobody else owns. A tournament or benchmark game is dispatched by
/// its tournament, with its concurrency; a broadcast slot or request game is played by the broadcast runner, paced.
/// </summary>
public class GameRunnerRestartTests
{
    private static readonly PlayerDescriptor RandomPlayer = new("random", "random");

    [Fact]
    public async Task Only_a_game_nobody_owns_is_played_after_a_restart()
    {
        await using var harness = new GameHarness(GameHarness.RealPlayers());
        var older = GameHarness.CreatedAt.AddHours(-1);
        var plain = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: GameHarness.CreatedAt);

        // These were saved earlier than the plain game, so an unfiltered runner would start them first.
        var tournamentGame = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: older);
        var slotGame = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: older.AddMinutes(1));
        var requestGame = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: older.AddMinutes(2));
        await using (var db = await harness.Factory.CreateDbContextAsync())
        {
            var game = await db.Games.SingleAsync(g => g.Id == tournamentGame);
            game.TournamentId = Guid.NewGuid();
            db.BroadcastSlots.Add(new BroadcastSlotEntity { Id = Guid.NewGuid(), ScheduleId = Guid.NewGuid(), GameId = slotGame });
            db.GameRequests.Add(new GameRequestEntity { Id = Guid.NewGuid(), ExternalId = "x1", GameId = requestGame });
            await db.SaveChangesAsync();
        }

        await harness.StartAsync();
        harness.FinishMigration();
        await WaitUntilFinishedAsync(harness, plain);

        Assert.NotNull((await harness.LoadGameAsync(plain)).StartedAt);
        foreach (var owned in new[] { tournamentGame, slotGame, requestGame })
        {
            var game = await harness.LoadGameAsync(owned);
            Assert.Null(game.StartedAt);
            Assert.Null(game.FinishedAt);
            Assert.Equal(0, game.PlyCount);
        }
    }

    private static async Task WaitUntilFinishedAsync(GameHarness harness, Guid gameId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while ((await harness.LoadGameAsync(gameId)).FinishedAt is null)
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
