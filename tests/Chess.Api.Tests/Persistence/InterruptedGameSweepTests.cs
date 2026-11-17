using Chess.Api.Broadcast;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Persistence;

public class InterruptedGameSweepTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = T0.AddHours(3);

    private static ArenaDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);

    [Fact]
    public async Task Sweep_closes_only_started_unfinished_games_as_interrupted()
    {
        var name = Guid.NewGuid().ToString();
        var running = Guid.NewGuid();
        var finished = Guid.NewGuid();
        var queued = Guid.NewGuid();
        var otherRunning = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            seed.Games.AddRange(
                new GameEntity { Id = running, CreatedAt = T0, StartedAt = T0, Result = "*" },
                new GameEntity
                {
                    Id = finished, CreatedAt = T0, StartedAt = T0, FinishedAt = T0.AddMinutes(5),
                    Result = "1-0", Termination = "checkmate",
                },
                new GameEntity { Id = queued, CreatedAt = T0, Result = "*" },
                new GameEntity { Id = otherRunning, CreatedAt = T0, StartedAt = T0.AddMinutes(1), Result = "*" });
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext(name);
        var swept = await InterruptedGameSweep.SweepAsync(context, Now, CancellationToken.None);

        Assert.Equal(2, swept);
        await using var read = NewContext(name);
        var games = await read.Games.ToDictionaryAsync(g => g.Id);

        foreach (var id in new[] { running, otherRunning })
        {
            Assert.Equal("*", games[id].Result);
            Assert.Equal("interrupted", games[id].Termination);
            Assert.Equal(Now, games[id].FinishedAt);
        }

        Assert.Equal("1-0", games[finished].Result);
        Assert.Equal("checkmate", games[finished].Termination);
        Assert.Equal(T0.AddMinutes(5), games[finished].FinishedAt);

        Assert.Null(games[queued].StartedAt);
        Assert.Null(games[queued].FinishedAt);
        Assert.Null(games[queued].Termination);
    }

    [Fact]
    public async Task Sweep_is_a_no_op_on_the_second_run()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = NewContext(name))
        {
            seed.Games.Add(new GameEntity { Id = Guid.NewGuid(), CreatedAt = T0, StartedAt = T0, Result = "*" });
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext(name);
        Assert.Equal(1, await InterruptedGameSweep.SweepAsync(context, Now, CancellationToken.None));
        Assert.Equal(0, await InterruptedGameSweep.SweepAsync(context, Now.AddHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task Sweep_closes_a_never_started_game_that_a_broadcast_slot_or_a_request_owns_but_not_a_tournament_or_a_plain_one()
    {
        var name = Guid.NewGuid().ToString();
        var slotGame = Guid.NewGuid();
        var requestGame = Guid.NewGuid();
        var tournamentGame = Guid.NewGuid();
        var plainGame = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            seed.Games.AddRange(
                new GameEntity { Id = slotGame, CreatedAt = T0, Result = "*" },
                new GameEntity { Id = requestGame, CreatedAt = T0, Result = "*" },
                new GameEntity { Id = tournamentGame, TournamentId = Guid.NewGuid(), CreatedAt = T0, Result = "*" },
                new GameEntity { Id = plainGame, CreatedAt = T0, Result = "*" });
            seed.BroadcastSlots.Add(new BroadcastSlotEntity { Id = Guid.NewGuid(), ScheduleId = Guid.NewGuid(), GameId = slotGame });
            seed.GameRequests.Add(new GameRequestEntity { Id = Guid.NewGuid(), ExternalId = "r1", GameId = requestGame });
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext(name);
        var swept = await InterruptedGameSweep.SweepAsync(context, Now, CancellationToken.None);

        Assert.Equal(2, swept);
        await using var read = NewContext(name);
        var games = await read.Games.ToDictionaryAsync(g => g.Id);
        foreach (var id in new[] { slotGame, requestGame })
        {
            Assert.Equal("interrupted", games[id].Termination);
            Assert.Equal(Now, games[id].FinishedAt);
        }

        foreach (var id in new[] { tournamentGame, plainGame })
        {
            Assert.Null(games[id].Termination);
            Assert.Null(games[id].FinishedAt);
        }
    }
}
