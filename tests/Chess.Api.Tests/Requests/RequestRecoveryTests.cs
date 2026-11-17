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
            db.Games.Add(new GameEntity { Id = gameId, Result = "1-0", Termination = "checkmate", CreatedAt = DateTimeOffset.UnixEpoch,
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

    [Fact]
    public async Task A_finished_aborted_game_is_not_played_so_the_request_is_requeued()
    {
        using var app = new RequestApp();
        var id = await PlayingRequestAsync(app, "rec-4");
        var gameId = await AddAbortedGameAsync(app);

        await app.Queue.StartedAsync(id, gameId, default);
        await RequestRecovery.RecoverAsync(app.Factory, app.Queue, default);

        var snapshot = await app.Queue.SnapshotAsync(default);
        Assert.Equal(RequestRecovery.RestartReason, snapshot.Queued.Single().Reason);
        Assert.Empty(snapshot.Recent);
    }

    [Fact]
    public async Task A_finished_aborted_game_of_a_request_taken_twice_credits_the_payer()
    {
        using var app = new RequestApp();
        var id = await PlayingRequestAsync(app, "rec-5");
        await app.Queue.FailedAsync(id, "first failure", default);
        await app.Queue.TakeNextAsync(default);
        var gameId = await AddAbortedGameAsync(app);
        await app.Queue.StartedAsync(id, gameId, default);

        await RequestRecovery.RecoverAsync(app.Factory, app.Queue, default);

        var recent = Assert.Single((await app.Queue.SnapshotAsync(default)).Recent);
        Assert.Equal(RequestStatus.Failed, recent.Status);
        await using var db = await app.Factory.CreateDbContextAsync();
        Assert.Equal(1, db.PayerLedger.Count(l => l.RequestId == id && l.Kind == PayerLedger.CreditKind));
    }

    [Fact]
    public async Task The_queue_takes_nothing_until_the_recovery_has_opened_its_gate()
    {
        using var app = new RequestApp();
        await app.Intake.AcceptAsync(Chat.Paid("rec-6", "rec", 1, "USD", Matchup), default);
        var gate = new RequestRecoveryGate();
        var queue = new GameRequestQueue(app.Factory, Microsoft.Extensions.Options.Options.Create(new RequestOptions()),
            TimeProvider.System, gate);

        var take = queue.TakeNextAsync(default);
        await Task.Delay(100);
        Assert.False(take.IsCompleted, "the runner must not take a request before the recovery has finished");

        gate.Open();
        Assert.NotNull(await take.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static async Task<Guid> AddAbortedGameAsync(RequestApp app)
    {
        var gameId = Guid.NewGuid();
        await using var db = await app.Factory.CreateDbContextAsync();
        db.Games.Add(new GameEntity { Id = gameId, Result = "*", Termination = "aborted", CreatedAt = DateTimeOffset.UnixEpoch,
            FinishedAt = DateTimeOffset.UnixEpoch.AddMinutes(5) });
        await db.SaveChangesAsync();
        return gameId;
    }
}
