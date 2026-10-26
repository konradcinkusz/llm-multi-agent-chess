using Chess.Api.Requests;
using System.Net;
using Xunit;

namespace Chess.Api.Tests.Requests.YouTube;

/// <summary>The poller against recorded YouTube responses (03 §15.5, V5): mapping, paging, intervals, token refresh and back-offs.</summary>
public sealed class YouTubePollerTests
{
    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task Super_chat_with_a_command_is_a_deposit_with_its_command()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chat = Single(harness, "LCC.fixture-superchat-command");
        Assert.Equal("youtube", chat.Source);
        Assert.Equal("UCfixturepayer0001", chat.PayerId);
        Assert.Equal("Ada", chat.PayerDisplay);
        Assert.Equal(5_000_000, chat.AmountMicros);
        Assert.Equal("USD", chat.Currency);
        Assert.Equal("!g c1 vs d3 think", chat.Text);
    }

    [Fact]
    public async Task Super_chat_without_a_command_is_still_a_deposit_with_its_comment()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chat = Single(harness, "LCC.fixture-superchat-plain");
        Assert.Equal(2_000_000, chat.AmountMicros);
        Assert.Equal("EUR", chat.Currency);
        Assert.Equal("Thanks for the stream", chat.Text);
    }

    [Fact]
    public async Task Plain_g_command_has_no_amount_so_it_spends_credit()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chat = Single(harness, "LCC.fixture-text-command");
        Assert.Null(chat.AmountMicros);
        Assert.Null(chat.Currency);
        Assert.Equal("!g c1 vs d3", chat.Text);
        Assert.Equal("UCfixturepayer0001", chat.PayerId);
    }

    [Fact]
    public async Task Ignored_messages_are_not_handed_to_the_intake()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);

        // "hello chat" (plain text), a new sponsor, and a Super Chat with no currency are not recorded.
        Assert.Equal(
            new[] { "LCC.fixture-superchat-command", "LCC.fixture-superchat-plain", "LCC.fixture-text-command" },
            harness.Intake.Events.Select(chat => chat.ExternalId).ToArray());
    }

    [Fact]
    public async Task Paging_follows_next_page_token_and_the_token_is_fetched_once()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-2.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);
        await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chatCalls = harness.Http.Requests.Where(request => request.Url.Contains("liveChat/messages")).ToList();
        Assert.Equal(2, chatCalls.Count);
        Assert.DoesNotContain("pageToken", chatCalls[0].Url);
        Assert.Contains("pageToken=page-2", chatCalls[1].Url);
        Assert.Single(harness.Http.Requests, request => request.Url.StartsWith("https://oauth.test/token"));
        Assert.Equal(4, harness.Intake.Events.Count);
    }

    [Fact]
    public async Task Poll_interval_is_taken_from_the_response_and_never_goes_below_the_minimum()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-2.json");

        var first = await harness.Poller.RunOnceAsync(CancellationToken.None);
        var second = await harness.Poller.RunOnceAsync(CancellationToken.None);

        // Page 1 suggests 60 s, which is honoured. Page 2 suggests 10 s, which is raised to the 45 s minimum.
        Assert.Equal(TimeSpan.FromSeconds(60), first);
        Assert.Equal(Minimum, second);
    }

    [Fact]
    public async Task Unauthorized_refreshes_the_token_and_retries_the_same_page()
    {
        var harness = new YouTubeHarness();
        harness.Http.Respond(HttpStatusCode.OK, "oauth-token.json");
        harness.Http.Respond(HttpStatusCode.OK, "broadcasts-active.json");
        harness.Http.Respond(HttpStatusCode.Unauthorized, "chat-unauthorized.json");
        harness.Http.Respond(HttpStatusCode.OK, "oauth-token-rotated.json");
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chatCalls = harness.Http.Requests.Where(request => request.Url.Contains("liveChat/messages")).ToList();
        Assert.Equal(2, chatCalls.Count);
        Assert.Equal("Bearer fixture-access-token-1", chatCalls[0].Authorization);
        Assert.Equal("Bearer fixture-access-token-2", chatCalls[1].Authorization);
        Assert.Equal(chatCalls[0].Url, chatCalls[1].Url);
        Assert.Equal(2, harness.Http.Requests.Count(request => request.Url.StartsWith("https://oauth.test/token")));
        Assert.Equal(3, harness.Intake.Events.Count);
    }

    [Fact]
    public async Task Quota_exhausted_backs_off_for_the_quota_delay_and_records_nothing()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.Forbidden, "chat-quota-exhausted.json");

        var delay = await harness.Poller.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(3600), delay);
        Assert.Empty(harness.Intake.Events);
    }

    [Fact]
    public async Task No_active_broadcast_is_idle_with_a_single_lookup()
    {
        var harness = new YouTubeHarness();
        harness.Http.Respond(HttpStatusCode.OK, "oauth-token.json");
        harness.Http.Respond(HttpStatusCode.OK, "broadcasts-none.json");

        var delay = await harness.Poller.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
        Assert.Equal(2, harness.Http.Requests.Count);
        Assert.DoesNotContain(harness.Http.Requests, request => request.Url.Contains("liveChat/messages"));
    }

    [Fact]
    public async Task A_chat_that_has_ended_goes_back_to_looking_for_a_broadcast()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.Forbidden, "chat-ended.json");
        harness.Http.Respond(HttpStatusCode.OK, "broadcasts-none.json");

        var first = await harness.Poller.RunOnceAsync(CancellationToken.None);
        var second = await harness.Poller.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(60), first);
        Assert.Equal(TimeSpan.FromSeconds(60), second);
        Assert.Contains("broadcastStatus=active", harness.Http.Requests[^1].Url);
        Assert.Contains("liveBroadcasts", harness.Http.Requests[^1].Url);
    }

    [Fact]
    public async Task Server_errors_back_off_exponentially_and_then_read_the_same_page_again()
    {
        var harness = new YouTubeHarness();
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.InternalServerError, "chat-backend-error.json");
        harness.Http.Respond(HttpStatusCode.InternalServerError, "chat-backend-error.json");
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        var first = await harness.Poller.RunOnceAsync(CancellationToken.None);
        var second = await harness.Poller.RunOnceAsync(CancellationToken.None);
        var third = await harness.Poller.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(15), first);
        Assert.Equal(TimeSpan.FromSeconds(30), second);
        Assert.Equal(TimeSpan.FromSeconds(60), third);
        Assert.Equal(3, harness.Intake.Events.Count);
    }

    [Fact]
    public async Task A_failed_intake_keeps_the_page_so_it_is_read_again()
    {
        var harness = new YouTubeHarness { Intake = { FailuresLeft = 1 } };
        harness.RespondWithActiveBroadcast();
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");
        harness.Http.Respond(HttpStatusCode.OK, "chat-page-1.json");

        var first = await harness.Poller.RunOnceAsync(CancellationToken.None);
        var second = await harness.Poller.RunOnceAsync(CancellationToken.None);

        var chatCalls = harness.Http.Requests.Where(request => request.Url.Contains("liveChat/messages")).ToList();
        Assert.Equal(TimeSpan.FromSeconds(15), first);
        Assert.Equal(TimeSpan.FromSeconds(60), second);
        Assert.Equal(chatCalls[0].Url, chatCalls[1].Url);
        Assert.Equal(3, harness.Intake.Events.Count);
    }

    [Fact]
    public async Task A_refused_refresh_backs_off_without_reading_the_chat()
    {
        var harness = new YouTubeHarness();
        harness.Http.Respond(HttpStatusCode.BadRequest, "oauth-token-invalid-grant.json");

        var delay = await harness.Poller.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(15), delay);
        Assert.Single(harness.Http.Requests);
        Assert.Empty(harness.Intake.Events);
    }

    private static ChatEvent Single(YouTubeHarness harness, string externalId) =>
        harness.Intake.Events.Single(chat => chat.ExternalId == externalId);
}
