using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

/// <summary>The viewer-request panels and the replays of the OBS page (03 §15.6), against canned API answers.</summary>
public class LiveRequestPanelsTests
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const string AfterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    private static readonly string Current = LiveFixtures.Slot(16, "gpt", "claude", "running", LiveBroadcastServer.GameId);

    [Fact]
    public void WithoutTheRequestsApiNoPanelIsDrawnAndTheStreamStillWorks()
    {
        // The default fake answers 404 to every request route, as an arena without viewer-paid games does.
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-queue]"));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-menu]"));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-link]"));
        Assert.Equal(32, cut.FindAll("[data-testid=broadcast-board] [data-piece]").Count);
    }

    [Fact]
    public void AQueueThatFailsIsNotAnErrorOnTheStream()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null"), QueueFails = true };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-queue]"));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-link]"));
        Assert.DoesNotContain("HTTP", cut.Markup);
    }

    [Fact]
    public void TheQueueShowsTheNextRequestsWithThePayerAndTheWait()
    {
        var queue = LiveFixtures.Queue(
            queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1) + "," + LiveFixtures.Request("Bo", "claude", "gemini", "queued", 2),
            wait: 12);
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null"), Queue = queue, Menu = Menu() };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=broadcast-queue] li").Count));
        var rows = cut.FindAll("[data-testid=broadcast-queue] li").Select(row => row.TextContent.Trim()).ToArray();
        Assert.Equal("1", rows[0][..1]);
        Assert.Contains("Ana · GPT vs Claude", rows[0]);
        Assert.Contains("Bo · Claude vs Gemini", rows[1]);
        Assert.Equal("about 12 min", cut.Find("[data-testid=broadcast-wait]").TextContent);
    }

    [Fact]
    public void ARejectionNamesThePayerAndTheMenuAndShowsNoFreeText()
    {
        var recent = LiveFixtures.Request("Cy", "gpt", "claude", "rejected", reason: "price above the largest tier");
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", Current, "[]", "null"),
            Queue = LiveFixtures.Queue(recent: recent),
            Menu = Menu(),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-rejection]")));
        Assert.Equal("@Cy: credit kept, see the menu", cut.Find("[data-testid=broadcast-rejection]").TextContent);
        Assert.DoesNotContain("largest tier", cut.Markup);
    }

    [Fact]
    public void TheMenuListsEachCodeWithItsCostPerSideAndHowToOrder()
    {
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", Current, "[]", "null"),
            Queue = LiveFixtures.Queue(),
            Menu = Menu(),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=broadcast-menu] li").Count));
        var rows = cut.FindAll("[data-testid=broadcast-menu] li").Select(row => row.TextContent).ToArray();
        Assert.Contains("c1", rows[0]);
        Assert.Contains("$0.40 a side", rows[0]);
        Assert.Contains("$2.00 a side", rows[1]);
        Assert.Contains("!g <white> vs <black> [style]", cut.Find("[data-testid=broadcast-order]").TextContent);
    }

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
    public void WithAnEmptyQueueTheStreamReplaysAStoredGameAtTheBroadcastPace()
    {
        var finished = LiveFixtures.Summary("finished", "1-0", "checkmate", 2);
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", "null", "[]", "null"),
            Schedules = LiveFixtures.PacedSchedules("running", 48, 16, moveDelayMs: 5, gameGapSeconds: 0),
            Queue = LiveFixtures.Queue(),
            Games = LiveFixtures.Page(finished),
            GameDetail = LiveFixtures.Detail("finished", "1-0", "checkmate",
                ("white", "e4", AfterE4), ("black", "e5", AfterE5)),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal("Replay of a past game", cut.Find("[data-testid=broadcast-status]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-intermission]"));
        cut.WaitForAssertion(() => Assert.Equal(new[] { "e5", "e7" }, Highlighted(cut)));
        Assert.False(server.StreamOpen);
    }

    [Fact]
    public void AQueuedRequestStopsTheReplayAndTheIntermissionReturns()
    {
        var finished = LiveFixtures.Summary("finished", "1-0", "checkmate", 2);
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", "null", "[]", "null"),
            Schedules = LiveFixtures.PacedSchedules("running", 48, 16, moveDelayMs: 5, gameGapSeconds: 0),
            Queue = LiveFixtures.Queue(),
            Games = LiveFixtures.Page(finished),
            GameDetail = LiveFixtures.Detail("finished", "1-0", "checkmate", ("white", "e4", AfterE4)),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Equal("Replay of a past game", cut.Find("[data-testid=broadcast-status]").TextContent));

        server.Queue = LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1), wait: 0);

        cut.WaitForAssertion(() => Assert.NotEqual("Replay of a past game", cut.Find("[data-testid=broadcast-status]").TextContent));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
    }

    [Fact]
    public void TheRequestsAreNotReplayedOverWhenTheQueueIsNotEmpty()
    {
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", "null", "[]", "null"),
            Schedules = LiveFixtures.PacedSchedules("running", 48, 16, moveDelayMs: 5, gameGapSeconds: 0),
            Queue = LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1), wait: 0),
            Games = LiveFixtures.Page(LiveFixtures.Summary("finished", "1-0", "checkmate", 2)),
            GameDetail = LiveFixtures.Detail("finished", "1-0", "checkmate", ("white", "e4", AfterE4)),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        Assert.DoesNotContain(server.Handler.Requests, request => request.Target.StartsWith("/api/games?", StringComparison.Ordinal));
        Assert.Equal("LLM Chess 24h", cut.Find("[data-testid=broadcast-title]").TextContent);
    }

    private static string Menu() => LiveFixtures.Menu(("c1", "GPT", 0.4), ("d3", "Claude", 2.0));

    private static string[] Highlighted(IRenderedComponent<LiveBroadcastPage> cut) =>
        cut.FindAll("[data-testid=broadcast-board] [data-highlight]")
            .Select(square => square.GetAttribute("data-highlight")!)
            .Order()
            .ToArray();

    private static void Wire(BunitContext ctx, LiveBroadcastServer server)
    {
        var client = Canned.Client(server.Handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
        ctx.Services.AddSingleton<IHttpClientFactory>(new ArenaClientFactory(client));
    }

    private sealed class ArenaClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == "ArenaApiClient" ? client : throw new InvalidOperationException($"No client named '{name}'.");
    }

    private static IRenderedComponent<LiveBroadcastPage> Render(BunitContext ctx)
    {
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/broadcast");
        return ctx.Render<LiveBroadcastPage>(parameters =>
        {
            parameters.Add(page => page.PollInterval, TimeSpan.FromMilliseconds(20));
            parameters.Add(page => page.ReconnectDelay, TimeSpan.FromMilliseconds(10));
        });
    }
}
