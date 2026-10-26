using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class RequestRecoveryTests
{
    private const string Matchup = "!g fa vs fb";

    private static async Task<Guid> PlayingRequestAsync(RequestApp app, string externalId)
    {
        await app.Intake.AcceptAsync(Chat.Paid(externalId, "rec", 1, "USD", Matchup), default);
        var taken = await app.Queue.TakeNextAsync(default);
        return taken!.Id;
    }

    [Fact]
    public async Task A_playing_request_whose_game_never_finished_is_requeued_by_the_restart()
    {
        using var app = new RequestApp();
        var id = await PlayingRequestAsync(app, "rec-1");
        await app.Queue.StartedAsync(id, Guid.NewGuid(), default);

        var recovered = await RequestRecovery.RecoverAsync(app.Factory, app.Queue, default);

        Assert.Equal(1, recovered);
        var snapshot = await app.Queue.SnapshotAsync(default);
        Assert.Null(snapshot.Playing);
        Assert.Equal(RequestRecovery.RestartReason, snapshot.Queued.Single().Reason);
    }

    [Fact]
    public async Task A_playing_request_whose_game_finished_is_played_not_requeued()
    {
        using var app = new RequestApp();
        var id = await PlayingRequestAsync(app, "rec-2");
        var gameId = Guid.NewGuid();
        await using (var db = await app.Factory.CreateDbContextAsync())
        {
            db.Games.Add(new GameEntity { Id = gameId, Termination = "checkmate", CreatedAt = DateTimeOffset.UnixEpoch,
                FinishedAt = DateTimeOffset.UnixEpoch.AddMinutes(5) });
            await db.SaveChangesAsync();
        }

        await app.Queue.StartedAsync(id, gameId, default);
        await RequestRecovery.RecoverAsync(app.Factory, app.Queue, default);

        var snapshot = await app.Queue.SnapshotAsync(default);
        Assert.Empty(snapshot.Queued);
        Assert.Equal(RequestStatus.Played, snapshot.Recent.Single().Status);
    }

    [Fact]
    public async Task A_game_the_sweep_closed_as_interrupted_counts_as_unfinished()
    {
        using var app = new RequestApp();
        var id = await PlayingRequestAsync(app, "rec-3");
        var gameId = Guid.NewGuid();
        await using (var db = await app.Factory.CreateDbContextAsync())
        {
            db.Games.Add(new GameEntity { Id = gameId, Termination = InterruptedGameSweep.Termination,
                CreatedAt = DateTimeOffset.UnixEpoch, FinishedAt = DateTimeOffset.UnixEpoch.AddMinutes(5) });
            await db.SaveChangesAsync();
        }

        await app.Queue.StartedAsync(id, gameId, default);
        await RequestRecovery.RecoverAsync(app.Factory, app.Queue, default);

        Assert.Equal(RequestStatus.Queued, (await app.Queue.SnapshotAsync(default)).Queued.Single().Status);
    }
}
