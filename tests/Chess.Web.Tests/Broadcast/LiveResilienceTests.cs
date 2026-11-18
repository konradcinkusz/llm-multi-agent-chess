using Bunit;
using Chess.Web.Tests.Play;
using Xunit;
using static Chess.Web.Tests.Broadcast.LivePageHarness;

namespace Chess.Web.Tests.Broadcast;

/// <summary>
/// The OBS page is on screen for a day. A read that times out or hangs must never end the poll loop, and the intermission
/// countdown must follow the clock the page is given.
/// </summary>
public class LiveResilienceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    private static readonly string Pending =
        $"[{LiveFixtures.Slot(17, "gpt@high", "claude@low", "pending", start: "2026-10-24T19:00:00Z")}]";

    /// <summary>
    /// An HttpClient timeout is a TaskCanceledException raised while the page's own token is not cancelled. It used to leave
    /// the poll task faulted in a discarded task, so the stage froze for the rest of the day.
    /// </summary>
    [Fact]
    public void ThePollLoopSurvivesAnHttpClientTimeoutAndTheStageRecovers()
    {
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", "null", Pending, "null"),
            NowTimeouts = 2,
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Contains("Reconnecting to the arena", cut.Find("[data-testid=broadcast-link]").TextContent));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=broadcast-link]")));
        Assert.Equal(0, server.NowTimeouts);
    }

    [Fact]
    public void TheStageKeepsPollingAfterTheTimeoutWhenTheBroadcastChanges()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("idle", "null", "[]", "null"), NowTimeouts = 1 };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Contains("Off air", cut.Find("[data-testid=broadcast-intermission]").TextContent));

        server.Now = LiveFixtures.Now("paused", "null", "[]", "null");
        server.NowTimeouts = 1;

        cut.WaitForAssertion(() => Assert.Contains("Paused", cut.Find("[data-testid=broadcast-intermission]").TextContent));
    }

    /// <summary>A read that never answers is cut at <c>RequestTimeout</c>, so the next poll goes out long before 100 s.</summary>
    [Fact]
    public void AReadThatHangsIsCutAndThePollRecovers()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("paused", "null", "[]", "null"), NowHangs = 1 };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, parameters => parameters.Add(page => page.RequestTimeout, TimeSpan.FromMilliseconds(150)));

        cut.WaitForAssertion(() => Assert.Contains("Paused", cut.Find("[data-testid=broadcast-intermission]").TextContent),
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheCountdownReadsTheClockThePageIsGiven()
    {
        var clock = new ManualClock(Start.AddMinutes(-30));
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", Pending, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, parameters => parameters.Add(page => page.Clock, clock));

        cut.WaitForAssertion(() => Assert.Equal("Starts in 30:00", Countdown(cut)));

        clock.Advance(TimeSpan.FromMinutes(29).Add(TimeSpan.FromSeconds(30)));
        cut.WaitForAssertion(() => Assert.Equal("Starts in 00:30", Countdown(cut)));

        clock.Advance(TimeSpan.FromSeconds(30));
        cut.WaitForAssertion(() => Assert.Equal("Starts in a moment", Countdown(cut)));
    }

    [Fact]
    public void ACountdownOfAnHourOrMoreShowsHours()
    {
        var clock = new ManualClock(Start.AddHours(-2).AddSeconds(-5));
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", Pending, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, parameters => parameters.Add(page => page.Clock, clock));

        cut.WaitForAssertion(() => Assert.Equal("Starts in 2:00:05", Countdown(cut)));
    }

    [Fact]
    public void TheIntermissionNamesBothSidesWithTheirEffortLikeThePlayerBars()
    {
        var clock = new ManualClock(Start.AddMinutes(-1));
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", Pending, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, parameters => parameters.Add(page => page.Clock, clock));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        Assert.Contains("GPT · high vs Claude · low", cut.Find("[data-testid=broadcast-intermission]").TextContent);
        Assert.Equal("GPT · high", cut.Find("[data-testid=broadcast-white]").TextContent.Trim());
        Assert.Equal("Claude · low", cut.Find("[data-testid=broadcast-black]").TextContent.Trim());
        Assert.DoesNotContain("@", cut.Find("[data-testid=broadcast-intermission]").TextContent);
    }

    [Fact]
    public void UpNextNamesBothSidesWithTheirEffort()
    {
        var next = $"[{LiveFixtures.Slot(17, "gpt@high", "claude@low", "pending")}]";
        var current = LiveFixtures.Slot(16, "gemini", "random", "running", LiveBroadcastServer.GameId);
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", current, next, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-upnext] li")));
        var row = cut.Find("[data-testid=broadcast-upnext] li").TextContent.Trim();
        Assert.Matches(@"^\d{2}:\d{2}\s+GPT · high vs Claude · low$", row);
    }

    [Fact]
    public void TheHeaderClockReadsTheClockThePageIsGiven()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 24, 12, 34, 56, TimeSpan.Zero));
        var server = new LiveBroadcastServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, parameters => parameters.Add(page => page.Clock, clock));

        Assert.Equal(clock.GetLocalNow().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            cut.Find("[data-testid=broadcast-clock]").TextContent);
    }

    private static string Countdown(IRenderedComponent<Chess.Web.Components.Pages.Broadcast.LiveBroadcastPage> cut) =>
        cut.Find("[data-testid=broadcast-countdown]").TextContent;
}
