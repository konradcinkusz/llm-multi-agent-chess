using Bunit;
using Xunit;
using static Chess.Web.Tests.Broadcast.LivePageHarness;

namespace Chess.Web.Tests.Broadcast;

/// <summary>
/// The idle replays and the requester line of the OBS page (03 §15.6, docs/BROADCAST.md section 8), against canned API
/// answers. The leaderboard and request panels the owner removed in 0d1056b are not tested because they are not drawn.
/// </summary>
public class LiveReplayTests
{
    private static readonly string Current = LiveFixtures.Slot(16, "gpt", "claude", "running", LiveBroadcastServer.GameId);
    private static readonly string Pending = $"[{LiveFixtures.Slot(17, "claude", "gemini", "pending", start: "2026-10-24T19:00:00Z")}]";

    [Fact]
    public void TheStatusNamesTheRequesterWhenTheGameOnAirIsARequest()
    {
        var queue = LiveFixtures.Queue(
            playing: LiveFixtures.Request("Ana", "gpt", "claude", "playing", gameId: LiveBroadcastServer.GameId));
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", Current, "[]", "null"),
            Queue = queue,
            GameDetail = LiveFixtures.Detail("running", "*", null, ("white", "e4", AfterE4)),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.StartsWith("Requested by Ana · ", cut.Find("[data-testid=broadcast-status]").TextContent));
    }

    [Fact]
    public void AQueueThatFailsIsNotAnErrorOnTheStream()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null"), QueueFails = true };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-link]"));
        Assert.DoesNotContain("HTTP", cut.Markup);
    }

    [Fact]
    public void WithAnEmptyQueueAndNoFurtherGameTheStreamReplaysAStoredGameAtTheBroadcastPace()
    {
        var server = ReplayServer(next: "[]", queue: LiveFixtures.Queue());
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-intermission]"));
        cut.WaitForAssertion(() => Assert.Equal(new[] { "e5", "e7" }, Highlighted(cut)));
        Assert.False(server.StreamOpen);
    }

    [Fact]
    public void AQueuedRequestStopsTheReplayAndTheIntermissionReturns()
    {
        var server = ReplayServer(next: "[]", queue: LiveFixtures.Queue());
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Equal(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent));

        server.Queue = LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1), wait: 0);

        cut.WaitForAssertion(() => Assert.NotEqual(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
    }

    [Fact]
    public void NoReplayPlaysWhileARequestIsQueued()
    {
        var server = ReplayServer(next: "[]",
            queue: LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1), wait: 0));
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        // Wait for the queue to be read more than once: until then the page cannot know whether a request waits.
        cut.WaitForAssertion(() => Assert.True(QueueReads(server) >= 3));
        Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]"));
        Assert.False(GamesListRead(server));
    }

    /// <summary>
    /// M6: before a scheduled game the stage shows the intermission card with the countdown, not a replay. The replay loop
    /// never starts, so the card is not pre-empted and no stored game is read.
    /// </summary>
    [Fact]
    public void BeforeAScheduledGameTheIntermissionAndItsCountdownShowAndNoReplayPlays()
    {
        var server = ReplayServer(next: Pending, queue: LiveFixtures.Queue());
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.True(QueueReads(server) >= 3));
        Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]"));
        Assert.Single(cut.FindAll("[data-testid=broadcast-countdown]"));
        Assert.False(GamesListRead(server));
        Assert.NotEqual(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent);
    }

    /// <summary>A replay allowed by an empty schedule ends when a game is scheduled after all: the card returns.</summary>
    [Fact]
    public void AReplayYieldsToTheIntermissionWhenAGameGetsScheduled()
    {
        var server = ReplayServer(next: "[]", queue: LiveFixtures.Queue());
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Equal(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent));

        server.Now = LiveFixtures.Now("running", "null", Pending, "null");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        Assert.NotEqual(ReplayStatus, cut.Find("[data-testid=broadcast-status]").TextContent);
        Assert.Single(cut.FindAll("[data-testid=broadcast-countdown]"));
    }

    [Fact]
    public void NoReplayPlaysWhileTheBroadcastIsPaused()
    {
        var server = ReplayServer(next: "[]", queue: LiveFixtures.Queue());
        server.Now = LiveFixtures.Now("paused", "null", "[]", "null");
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Contains("Paused", cut.Find("[data-testid=broadcast-intermission]").TextContent));
        cut.WaitForAssertion(() => Assert.True(QueueReads(server) >= 3));
        Assert.False(GamesListRead(server));
    }

    /// <summary>
    /// While the replay loop looks for a game the intermission stays: hiding it while the loop only looks made the card
    /// flicker, and when no game was found it came back.
    /// </summary>
    [Fact]
    public void TheIntermissionStaysWhileTheReplayLoopIsStillLookingForAGame()
    {
        var server = ReplayServer(next: "[]", queue: LiveFixtures.Queue());
        server.GamesHang = true;
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.True(GamesListRead(server)));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        Assert.Contains("The schedule has no further games.", cut.Find("[data-testid=broadcast-intermission]").TextContent);
    }

    private static LiveBroadcastServer ReplayServer(string next, string queue) => new()
    {
        Now = LiveFixtures.Now("running", "null", next, "null"),
        Schedules = LiveFixtures.PacedSchedules("running", 48, 16, moveDelayMs: 5, gameGapSeconds: 0),
        Queue = queue,
        Games = LiveFixtures.Page(LiveFixtures.Summary("finished", "1-0", "checkmate", 2)),
        GameDetail = LiveFixtures.Detail("finished", "1-0", "checkmate", ("white", "e4", AfterE4), ("black", "e5", AfterE5)),
    };
}
