using Chess.Api.Persistence;
using Chess.Api.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Requests;

/// <summary>
/// The owner's cancel changes a request only while it is queued. The change is one conditional update (TakeNext's pattern), so a
/// request the runner has taken is never both played and credited. The InMemory provider runs the checked-save branch; the
/// relational branch is one UPDATE ... WHERE status = 'queued' and needs PostgreSQL to exercise.
/// </summary>
public class RequestQueueCancelTests
{
    private const string Matchup = "!g fa vs fb";

    private static async Task<long> CreditsAsync(RequestApp app, Guid requestId)
    {
        await using var db = await app.Factory.CreateDbContextAsync();
        return await db.PayerLedger.AsNoTracking()
            .Where(l => l.RequestId == requestId && l.Kind == PayerLedger.CreditKind)
            .SumAsync(l => l.AmountMicros);
    }

    private static async Task<Guid> QueuedRequestAsync(RequestApp app, string externalId)
    {
        await app.Intake.AcceptAsync(Chat.Paid(externalId, "cancel", 1, "USD", Matchup), default);
        var snapshot = await app.Queue.SnapshotAsync(default);
        return snapshot.Queued.Single().Id;
    }

    [Fact]
    public async Task Cancelling_a_queued_request_credits_it_once()
    {
        using var app = new RequestApp();
        var id = await QueuedRequestAsync(app, "cancel-1");

        var first = await app.Queue.CancelAsync(id, default);
        var second = await app.Queue.CancelAsync(id, default);

        Assert.Equal(RequestStatus.Cancelled, first.Request!.Status);
        Assert.NotNull(second.Error);
        Assert.Equal(Money.FromUnits(1), await CreditsAsync(app, id));
    }

    [Fact]
    public async Task A_request_the_runner_has_taken_cannot_be_cancelled_and_is_not_credited()
    {
        using var app = new RequestApp();
        var id = await QueuedRequestAsync(app, "cancel-2");
        await app.Queue.TakeNextAsync(default);

        var outcome = await app.Queue.CancelAsync(id, default);

        Assert.True(outcome.Found);
        Assert.Null(outcome.Request);
        Assert.Contains(RequestStatus.Playing, outcome.Error);
        Assert.Equal(0, await CreditsAsync(app, id));
        Assert.Equal(RequestStatus.Playing, (await app.Queue.SnapshotAsync(default)).Playing!.Status);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_taken()
    {
        using var app = new RequestApp();
        var id = await QueuedRequestAsync(app, "cancel-3");
        await app.Queue.CancelAsync(id, default);

        Assert.Null(await app.Queue.TakeNextAsync(default));
        Assert.Equal(Money.FromUnits(1), await CreditsAsync(app, id));
    }

    [Fact]
    public async Task Cancelling_an_unknown_request_finds_nothing()
    {
        using var app = new RequestApp();

        var outcome = await app.Queue.CancelAsync(Guid.NewGuid(), default);

        Assert.False(outcome.Found);
    }
}
