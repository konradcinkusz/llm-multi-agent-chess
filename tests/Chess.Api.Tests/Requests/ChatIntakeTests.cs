using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class ChatIntakeTests
{
    /// <summary>The fake menu's classic matchup costs the $1 tier: its price is what a viewer pays.</summary>
    private const string Matchup = "!g fa vs fb";

    private static async Task<double> BalanceAsync(RequestApp app, string payerId)
    {
        await using var db = await app.Factory.CreateDbContextAsync();
        return Money.ToUnits(await PayerLedger.BalanceMicrosAsync(db, payerId, CancellationToken.None));
    }

    [Fact]
    public async Task A_rejected_request_keeps_the_credit_and_a_later_plain_command_spends_it()
    {
        using var app = new RequestApp();

        var first = await app.Intake.AcceptAsync(Chat.Paid("sc-1", "alice", 0.5, "USD", Matchup), default);
        Assert.Equal(0.5, first.DepositUsd);
        Assert.Equal(RequestStatus.Rejected, first.Request!.Status);
        Assert.Equal(0.5, await BalanceAsync(app, "alice"));

        var topUp = await app.Intake.AcceptAsync(Chat.Paid("sc-2", "alice", 0.5, "USD", "thanks"), default);
        Assert.Equal(1.0, await BalanceAsync(app, "alice"));
        Assert.Null(topUp.Request);

        var spend = await app.Intake.AcceptAsync(Chat.Plain("chat-3", "alice", Matchup), default);
        Assert.Equal(RequestStatus.Queued, spend.Request!.Status);
        Assert.Equal(1, spend.Request.QueuePosition);
        Assert.Equal(0, await BalanceAsync(app, "alice"));
    }

    [Fact]
    public async Task A_duplicate_external_id_changes_nothing()
    {
        using var app = new RequestApp();
        var message = Chat.Paid("sc-dup", "bob", 1, "USD", Matchup);

        var first = await app.Intake.AcceptAsync(message, default);
        var second = await app.Intake.AcceptAsync(message, default);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Null(second.Request);
        Assert.Equal(0, await BalanceAsync(app, "bob"));

        await using var db = await app.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.GameRequests.CountAsync());
        Assert.Equal(2, await db.PayerLedger.CountAsync());
    }

    [Fact]
    public async Task An_unknown_currency_is_recorded_at_zero_with_its_reason()
    {
        using var app = new RequestApp();

        var result = await app.Intake.AcceptAsync(Chat.Paid("sc-zar", "carol", 50, "ZZZ", "hello"), default);

        Assert.Equal(0, result.DepositUsd);
        Assert.Equal(0, await BalanceAsync(app, "carol"));
        await using var db = await app.Factory.CreateDbContextAsync();
        var line = await db.PayerLedger.SingleAsync(l => l.ExternalId == "sc-zar");
        Assert.Equal("currency not configured", line.Reason);
        Assert.Equal("ZZZ", line.Currency);
    }

    [Fact]
    public async Task A_message_that_is_not_a_command_records_only_what_was_paid()
    {
        using var app = new RequestApp();

        var result = await app.Intake.AcceptAsync(Chat.Plain("chat-plain", "dan", "gg"), default);

        Assert.Null(result.Request);
        Assert.Null(result.DepositUsd);
        await using var db = await app.Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.GameRequests.CountAsync());
        Assert.Equal(0, await db.PayerLedger.CountAsync());
    }

    [Fact]
    public async Task An_unknown_menu_code_is_rejected_with_a_generic_reason_and_the_credit_is_kept()
    {
        using var app = new RequestApp();

        var result = await app.Intake.AcceptAsync(Chat.Paid("sc-bad", "erin", 1, "USD", "!g nope vs fb"), default);

        Assert.Equal(RequestStatus.Rejected, result.Request!.Status);
        Assert.Equal("Not a menu code.", result.Request.Reason);
        Assert.Equal(1.0, await BalanceAsync(app, "erin"));
    }

    [Fact]
    public async Task A_cancelled_request_is_credited_back_once()
    {
        using var app = new RequestApp();
        var queued = await app.Intake.AcceptAsync(Chat.Paid("sc-cancel", "fred", 1, "USD", Matchup), default);
        Assert.Equal(0, await BalanceAsync(app, "fred"));

        var cancelled = await app.Queue.CancelAsync(queued.Request!.Id, default);
        var again = await app.Queue.CancelAsync(queued.Request.Id, default);

        Assert.Equal(RequestStatus.Cancelled, cancelled.Request!.Status);
        Assert.Equal(1.0, await BalanceAsync(app, "fred"));
        Assert.Null(again.Request);
        Assert.True(again.Found);
        Assert.NotNull(again.Error);
        Assert.Equal(1.0, await BalanceAsync(app, "fred"));
    }

    [Fact]
    public async Task A_failed_request_is_requeued_once_and_then_credited()
    {
        using var app = new RequestApp();
        var queued = await app.Intake.AcceptAsync(Chat.Paid("sc-fail", "gus", 1, "USD", Matchup), default);

        var firstTake = await app.Queue.TakeNextAsync(default);
        await app.Queue.FailedAsync(firstTake!.Id, "the stream dropped", default);
        Assert.Equal(RequestStatus.Queued, (await app.Queue.SnapshotAsync(default)).Queued.Single().Status);

        var secondTake = await app.Queue.TakeNextAsync(default);
        Assert.Equal(queued.Request!.Id, secondTake!.Id);
        await app.Queue.FailedAsync(secondTake.Id, "the stream dropped again", default);

        var snapshot = await app.Queue.SnapshotAsync(default);
        Assert.Empty(snapshot.Queued);
        Assert.Equal(RequestStatus.Failed, snapshot.Recent.Single().Status);
        Assert.Equal(1.0, await BalanceAsync(app, "gus"));
    }

    [Fact]
    public async Task A_played_request_is_marked_played_and_is_not_credited()
    {
        using var app = new RequestApp();
        await app.Intake.AcceptAsync(Chat.Paid("sc-play", "hal", 1, "USD", Matchup), default);
        var taken = await app.Queue.TakeNextAsync(default);

        await app.Queue.StartedAsync(taken!.Id, Guid.NewGuid(), default);
        await app.Queue.PlayedAsync(taken.Id, default);

        var snapshot = await app.Queue.SnapshotAsync(default);
        Assert.Null(snapshot.Playing);
        Assert.Equal(RequestStatus.Played, snapshot.Recent.Single().Status);
        Assert.Equal(0, await BalanceAsync(app, "hal"));
    }

    [Fact]
    public async Task The_queue_is_first_in_first_out_with_positions_and_an_estimated_wait()
    {
        using var app = new RequestApp();
        await app.Intake.AcceptAsync(Chat.Paid("q-1", "ivy", 1, "USD", Matchup), default);
        await app.Intake.AcceptAsync(Chat.Paid("q-2", "jay", 1, "USD", Matchup), default);
        await app.Intake.AcceptAsync(Chat.Paid("q-3", "kim", 1, "USD", Matchup), default);

        var before = await app.Queue.SnapshotAsync(default);
        Assert.Equal(new[] { 1, 2, 3 }, before.Queued.Select(r => r.QueuePosition!.Value));
        Assert.Equal(new[] { "ivy", "jay", "kim" }, before.Queued.Select(r => r.PayerDisplay));
        Assert.Equal(3 * 20, before.EstimatedWaitMinutes);

        var taken = await app.Queue.TakeNextAsync(default);
        var after = await app.Queue.SnapshotAsync(default);

        Assert.Equal(taken!.PayerDisplay, after.Playing!.PayerDisplay);
        Assert.Equal(new[] { 1, 2 }, after.Queued.Select(r => r.QueuePosition!.Value));
        Assert.Equal(3 * 20, after.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task Taking_from_an_empty_queue_returns_null()
    {
        using var app = new RequestApp();

        Assert.Null(await app.Queue.TakeNextAsync(default));
    }

    [Fact]
    public async Task The_quote_is_the_tier_of_the_matchup_and_an_unknown_code_is_refused()
    {
        using var app = new RequestApp();

        var quote = await app.Menu.QuoteAsync("fa", "fb", default);
        var unknown = await app.Menu.QuoteAsync("nope", "fb", default);
        var think = await app.Menu.QuoteAsync("fa~think", "fb~think", default);

        Assert.Equal(1.0, quote.Quote!.PriceUsd);
        Assert.Equal("fake-a", quote.Quote.White);
        Assert.Equal("Not a menu code.", unknown.Error);
        Assert.Equal("fake-a~think", think.Quote!.White);
        Assert.Equal(1.0, think.Quote.PriceUsd);
    }

    [Fact]
    public async Task A_queued_request_keeps_the_payers_name_and_the_matchup_but_not_the_message_text()
    {
        using var app = new RequestApp();
        var result = await app.Intake.AcceptAsync(Chat.Paid("sc-text", "lee", 1, "USD", "!g fa vs fb nice stream"), default);

        Assert.Equal("lee", result.Request!.PayerDisplay);
        Assert.Equal("fake-a", result.Request.White);
        Assert.Equal("fake-b", result.Request.Black);
        Assert.DoesNotContain("nice stream", result.Request.Reason ?? string.Empty);
    }
}
