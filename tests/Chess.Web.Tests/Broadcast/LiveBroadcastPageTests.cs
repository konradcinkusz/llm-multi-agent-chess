using System.Net;
using System.Reflection;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Layout;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class LiveBroadcastPageTests
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const string AfterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    private static readonly string Current = LiveFixtures.Slot(16, "gpt", "claude", "running", LiveBroadcastServer.GameId);

    [Fact]
    public void ShowsTheCurrentGamesPlayersBoardAndHeaderFromTheStub()
    {
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", Current, "[]", "null"),
            GameDetail = LiveFixtures.Detail("running", "*", null),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal("LLM Chess 24h — Game 17 of 48", cut.Find("[data-testid=broadcast-title]").TextContent));
        Assert.Equal("GPT", cut.Find("[data-testid=broadcast-white]").TextContent.Trim());
        Assert.Equal("Claude", cut.Find("[data-testid=broadcast-black]").TextContent.Trim());
        Assert.Equal(32, cut.FindAll("[data-testid=broadcast-board] [data-piece]").Count);
        Assert.Empty(cut.FindAll("[data-testid=broadcast-intermission]"));
        Assert.Empty(cut.FindAll("[data-testid=broadcast-notice]"));
    }

    [Fact]
    public async Task TheBoardAndTheMoveTailUpdateWhenAMoveIsPlayedOnTheStream()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        await server.SendAsync("MoveStarted", LiveFixtures.MoveStarted(1, "White", "gpt"));
        await server.SendAsync("MovePlayed", LiveFixtures.MovePlayed(1, "White", "e4", AfterE4));

        cut.WaitForAssertion(() => Assert.Equal(new[] { "e2", "e4" }, Highlighted(cut)));
        Assert.Contains("e4", cut.Find("[data-move='1']").TextContent);

        await server.SendAsync("MovePlayed", LiveFixtures.MovePlayed(2, "Black", "e5", AfterE5));

        cut.WaitForAssertion(() => Assert.Equal(new[] { "e5", "e7" }, Highlighted(cut)));
        Assert.Contains("e5", cut.Find("[data-move='1']").TextContent);
        server.EndStream();
    }

    [Fact]
    public void TheMoveTailShowsTheSansOfTheLoadedGameWhoseColoursAreInLowerCase()
    {
        // The server writes colours in lower case ("white"). The tail must still pair the moves by side.
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("running", Current, "[]", "null"),
            GameDetail = LiveFixtures.Detail("running", "*", null, ("white", "e4", AfterE4), ("black", "e5", AfterE5)),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal("e4", cut.Find("[data-move='1'] .bc-san").TextContent));
        Assert.Equal(new[] { "e4", "e5" }, cut.FindAll("[data-move='1'] .bc-san").Select(san => san.TextContent).ToArray());
    }

    [Fact]
    public void ShowsTheIntermissionCardWithTheNextGameWhenNothingIsOn()
    {
        var next = $"[{LiveFixtures.Slot(17, "claude", "gemini", "pending", start: "2026-10-24T19:00:00Z")}]";
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", next, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));
        var card = cut.Find("[data-testid=broadcast-intermission]").TextContent;
        Assert.Contains("Intermission", card);
        Assert.Contains("Claude vs Gemini", card);
        Assert.Equal("LLM Chess 24h", cut.Find("[data-testid=broadcast-title]").TextContent);
    }

    [Fact]
    public void ShowsAPausedCardNotAnIntermissionWhenTheBroadcastIsPausedBetweenGames()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("paused", "null", "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Contains("Paused", cut.Find("[data-testid=broadcast-intermission]").TextContent));
        Assert.Contains("will continue when it is resumed", cut.Find("[data-testid=broadcast-intermission]").TextContent);
    }

    [Fact]
    public void SaysOffAirWhenNoBroadcastIsRunning()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("idle", "null", "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Contains("Off air", cut.Find("[data-testid=broadcast-intermission]").TextContent));
        Assert.Contains("No broadcast is running.", cut.Find("[data-testid=broadcast-intermission]").TextContent);
    }

    [Fact]
    public void ShowsTheTechnicalBreakCardWhenTheRunnerIsBackingOff()
    {
        var notice = "\"Copilot is rate limited. Retrying in 30 s.\"";
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", "[]", notice) };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-notice]")));
        Assert.Contains("Technical break", cut.Find("[data-testid=broadcast-notice]").TextContent);
        Assert.Contains("Copilot is rate limited. Retrying in 30 s.", cut.Find("[data-testid=broadcast-notice]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=broadcast-intermission]"));
    }

    [Fact]
    public void TheLeaderboardShowsEightModelsByDefaultAndTheTopNWhenAsked()
    {
        var server = new LiveBroadcastServer
        {
            Now = LiveFixtures.Now("idle", "null", "[]", "null"),
            Leaderboard = LiveFixtures.Leaderboard(LiveFixtures.Ratings),
        };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var defaults = Render(ctx);
        WaitForRows(defaults, 8);
        Assert.Equal("m01", defaults.Find("[data-testid=broadcast-leaderboard] li[data-model]").GetAttribute("data-model"));

        var top = Render(ctx, "?top=3");
        WaitForRows(top, 3);
        Assert.Equal(new[] { "m01", "m02", "m03" }, top.FindAll("[data-testid=broadcast-leaderboard] li[data-model]")
            .Select(row => row.GetAttribute("data-model")!).ToArray());
    }

    [Fact]
    public void UpNextListsTheNextThreeGamesWithTheirStartTimes()
    {
        var next = $"[{LiveFixtures.Slot(17, "claude", "gemini", "pending")}," +
            $"{LiveFixtures.Slot(18, "gpt", "random", "pending")}," +
            $"{LiveFixtures.Slot(19, "gemini", "gpt", "pending")}]";
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, next, "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=broadcast-upnext] li").Count));
        var rows = cut.FindAll("[data-testid=broadcast-upnext] li").Select(row => row.TextContent.Trim()).ToArray();
        Assert.Matches(@"^\d{2}:\d{2}\s+Claude vs Gemini$", rows[0]);
        Assert.Matches(@"^\d{2}:\d{2}\s+GPT vs Random$", rows[1]);
        Assert.Matches(@"^\d{2}:\d{2}\s+Gemini vs GPT$", rows[2]);
    }

    [Fact]
    public async Task ReopensTheStreamWhenItClosesBeforeTheGameEnds()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        server.EndStream();

        cut.WaitForAssertion(() => Assert.True(server.StreamRequests >= 2));
        await server.SendAsync("MovePlayed", LiveFixtures.MovePlayed(1, "White", "e4", AfterE4));
        cut.WaitForAssertion(() => Assert.Equal(new[] { "e2", "e4" }, Highlighted(cut)));
    }

    [Fact]
    public async Task TheLeaderboardIsReadAgainWhenTheGameEnds()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", Current, "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        cut.WaitForAssertion(() => Assert.Equal(1, server.RatingsRequests));

        await server.SendAsync("GameFinished", LiveFixtures.GameFinished("1-0", "checkmate"));

        cut.WaitForAssertion(() => Assert.Equal(2, server.RatingsRequests));
        cut.WaitForAssertion(() => Assert.Contains("Result 1-0", cut.Find("[data-testid=broadcast-status]").TextContent));
    }

    [Fact]
    public void ASecondGameIsFollowedWhenTheBroadcastMovesOn()
    {
        var server = new LiveBroadcastServer { Now = LiveFixtures.Now("running", "null", "[]", "null") };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=broadcast-intermission]")));

        server.Now = LiveFixtures.Now("running", Current, "[]", "null");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=broadcast-intermission]")));
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        Assert.Equal("LLM Chess 24h — Game 17 of 48", cut.Find("[data-testid=broadcast-title]").TextContent);
    }

    [Fact]
    public void SaysItIsReconnectingWhenTheApiCannotBeReached()
    {
        var server = new LiveBroadcastServer { NowStatus = HttpStatusCode.InternalServerError };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Contains("Reconnecting to the arena", cut.Find("[data-testid=broadcast-link]").TextContent));
    }

    [Fact]
    public void TheDarkThemeIsTheDefault()
    {
        var server = new LiveBroadcastServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        Assert.True(cut.Find("[data-testid=broadcast-stage]").ClassList.Contains("bc-dark"));
    }

    [Fact]
    public void TheLightThemeIsChosenByTheQueryOption()
    {
        var server = new LiveBroadcastServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx, "?theme=light");

        Assert.True(cut.Find("[data-testid=broadcast-stage]").ClassList.Contains("bc-light"));
    }

    [Fact]
    public void TheClockShowsLocalTimeToTheSecond()
    {
        var server = new LiveBroadcastServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", cut.Find("[data-testid=broadcast-clock]").TextContent);
    }

    [Fact]
    public void PagesUseTheChromelessBroadcastLayout()
    {
        var layout = typeof(LiveBroadcastPage).GetCustomAttribute<LayoutAttribute>();

        Assert.Equal(typeof(BroadcastLayout), layout?.LayoutType);
    }

    private static string[] Highlighted(IRenderedComponent<LiveBroadcastPage> cut) =>
        cut.FindAll("[data-testid=broadcast-board] [data-highlight]")
            .Select(square => square.GetAttribute("data-highlight")!)
            .Order()
            .ToArray();

    [Fact]
    public void AProvisionalModelShowsADashForItsRatingNotThePlaceholderZero()
    {
        var leaderboard = """
            {"models":[
              {"model":"fake-b","rating":0,"ciLow":0,"ciHigh":0,"games":1,"score":0.5,"legality":1,"moveQuality":null,"quality":1,"arenaScore":50,"provisional":true},
              {"model":"random","rating":1512,"ciLow":1450,"ciHigh":1574,"games":40,"score":0.4,"legality":1,"moveQuality":null,"quality":1,"arenaScore":38.5,"provisional":false}
            ],"whiteAdvantage":0,"games":41,"moveQualityAvailable":false}
            """;
        var server = new LiveBroadcastServer { Leaderboard = leaderboard };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = Render(ctx);

        WaitForRows(cut, 2);
        var rows = cut.FindAll("[data-testid=broadcast-leaderboard] li[data-model]");
        Assert.Equal("random", rows[0].GetAttribute("data-model"));
        Assert.Equal("38.5", rows[0].QuerySelectorAll(".bc-num")[0].TextContent);
        Assert.Equal("1512", rows[0].QuerySelectorAll(".bc-num")[1].TextContent);
        Assert.Contains("provisional", rows[1].TextContent);
        Assert.Equal("50.0", rows[1].QuerySelectorAll(".bc-num")[0].TextContent);
        Assert.Equal("—", rows[1].QuerySelectorAll(".bc-num")[1].TextContent);
    }

    /// <summary>
    /// Renders the page at <c>/broadcast</c> plus <paramref name="query"/>, polling every 20 ms so a test sees a change to
    /// the stub within bUnit's wait. The query options are read from the navigation, as they are in the app.
    /// </summary>
    private static IRenderedComponent<LiveBroadcastPage> Render(BunitContext ctx, string query = "")
    {
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"http://localhost/broadcast{query}");
        return ctx.Render<LiveBroadcastPage>(parameters =>
        {
            parameters.Add(page => page.PollInterval, TimeSpan.FromMilliseconds(20));
            parameters.Add(page => page.ReconnectDelay, TimeSpan.FromMilliseconds(10));
        });
    }
    private static void WaitForRows(IRenderedComponent<LiveBroadcastPage> cut, int rows) =>
        cut.WaitForAssertion(() => Assert.Equal(rows, cut.FindAll("[data-testid=broadcast-leaderboard] li[data-model]").Count));

    private static void Wire(BunitContext ctx, LiveBroadcastServer server)
    {
        var client = Canned.Client(server.Handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
        ctx.Services.AddSingleton<IHttpClientFactory>(new ArenaClientFactory(client));
    }

    /// <summary>Answers only the name the page asks for, so a rename of that client shows up as a failing test.</summary>
    private sealed class ArenaClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == "ArenaApiClient" ? client : throw new InvalidOperationException($"No client named '{name}'.");
    }
}
