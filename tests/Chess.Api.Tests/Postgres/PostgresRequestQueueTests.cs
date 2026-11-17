using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Chess.Api.Tests.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// The paid-request queue on PostgreSQL. Taking a request is one conditional <c>UPDATE</c> there (<c>ExecuteUpdateAsync</c>), a branch
/// the InMemory provider never runs: of two takers that chose the same row, exactly one may change it.
/// </summary>
public class PostgresRequestQueueTests
{
    private const string Matchup = "!g fa vs fb";

    private static async Task<IntakeResult> PayAsync(PostgresApp app, string externalId)
    {
        var intake = app.Services.GetRequiredService<IChatIntake>();
        var result = await intake.AcceptAsync(Chat.Paid(externalId, "payer-" + externalId, 1, "USD", Matchup), default);
        Assert.Equal(RequestStatus.Queued, result.Request!.Status);
        return result;
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task Take_marks_the_oldest_queued_request_playing_with_one_attempt_and_a_start_time()
    {
        using var app = new PostgresApp(quietRequests: true);
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();
        var queue = app.Services.GetRequiredService<GameRequestQueue>();
        var first = await PayAsync(app, "q-1");
        var second = await PayAsync(app, "q-2");

        var taken = await queue.TakeNextAsync(default);

        Assert.NotNull(taken);
        Assert.Equal(first.Request!.Id, taken.Id);
        Assert.Equal("fake-a", taken.White);
        Assert.Equal("fake-b", taken.Black);
        await using var db = await app.Factory.CreateDbContextAsync();
        var row = await db.GameRequests.AsNoTracking().SingleAsync(r => r.Id == taken.Id);
        Assert.Equal(RequestStatus.Playing, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.StartedAt);
        Assert.Equal(RequestStatus.Queued, (await db.GameRequests.AsNoTracking().SingleAsync(r => r.Id == second.Request!.Id)).Status);

        var snapshot = await client.GetFromJsonAsync<RequestQueueDto>("/api/requests/queue");
        Assert.Equal(first.Request.Id, snapshot!.Playing!.Id);
        Assert.Equal([second.Request!.Id], snapshot.Queued.Select(r => r.Id));
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task Concurrent_takers_never_take_the_same_request_twice()
    {
        using var app = new PostgresApp(quietRequests: true);
        await app.WaitUntilReadyAsync();
        var queue = app.Services.GetRequiredService<GameRequestQueue>();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await PayAsync(app, $"c-{i}")).Request!.Id);
        }

        // Eight takers race for three requests: every request is taken once, and the other takers find the queue empty.
        var takers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => queue.TakeNextAsync(default))).ToArray();
        var results = await Task.WhenAll(takers);

        var taken = results.Where(r => r is not null).Select(r => r!.Id).ToList();
        Assert.Equal(3, taken.Count);
        Assert.Equal(ids, taken.ToHashSet());
        Assert.Equal(5, results.Count(r => r is null));

        await using var db = await app.Factory.CreateDbContextAsync();
        var rows = await db.GameRequests.AsNoTracking().ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(RequestStatus.Playing, row.Status);
            Assert.Equal(1, row.Attempts);
        });
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task A_failure_requeues_once_then_credits_the_payer_and_nothing_is_lost()
    {
        using var app = new PostgresApp(quietRequests: true);
        await app.WaitUntilReadyAsync();
        var queue = app.Services.GetRequiredService<GameRequestQueue>();
        var id = (await PayAsync(app, "f-1")).Request!.Id;

        var first = await queue.TakeNextAsync(default);
        await queue.FailedAsync(first!.Id, "gateway down", default);
        await using (var db = await app.Factory.CreateDbContextAsync())
        {
            var requeued = await db.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id);
            Assert.Equal(RequestStatus.Queued, requeued.Status);
            Assert.Null(requeued.StartedAt);
            Assert.Equal(1, requeued.Attempts);
        }

        var second = await queue.TakeNextAsync(default);
        Assert.Equal(id, second!.Id);
        await queue.FailedAsync(id, "gateway down again", default);

        await using var final = await app.Factory.CreateDbContextAsync();
        var failed = await final.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(RequestStatus.Failed, failed.Status);
        Assert.Equal(2, failed.Attempts);
        Assert.NotNull(failed.FinishedAt);
        Assert.Null(await queue.TakeNextAsync(default));
        // The deposit is still the payer's: the ledger holds it as credit.
        var ledger = await final.PayerLedger.AsNoTracking().ToListAsync();
        Assert.NotEmpty(ledger);
        Assert.True(ledger.Sum(entry => entry.AmountMicros) > 0, "the payer's balance must not be lost");
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task Cancel_and_the_queue_snapshot_answer_over_http()
    {
        using var app = new PostgresApp(quietRequests: true);
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        var simulated = await client.PostAsJsonAsync("/api/requests/simulate",
            new SimulateChatRequest("frank", "Frank", 1, "USD", Matchup));
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);
        var request = (await simulated.Content.ReadFromJsonAsync<IntakeResult>())!.Request!;

        var queue = await client.GetFromJsonAsync<RequestQueueDto>("/api/requests/queue");
        Assert.Equal([request.Id], queue!.Queued.Select(r => r.Id));
        Assert.Equal(1, queue.Queued[0].QueuePosition);

        var cancelled = await client.PostAsync($"/api/requests/{request.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var after = await client.GetFromJsonAsync<RequestQueueDto>("/api/requests/queue");
        Assert.Empty(after!.Queued);
        Assert.Equal(RequestStatus.Cancelled, after.Recent.Single().Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/requests/{request.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/requests/{Guid.NewGuid()}/cancel", null)).StatusCode);
    }
}
