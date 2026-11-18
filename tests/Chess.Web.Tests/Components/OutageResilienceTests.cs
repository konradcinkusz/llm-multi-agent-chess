using System.Net;
using System.Text;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Admin;
using Chess.Web.Components.Pages.Benchmark;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Components.Pages.HeadToHead;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Broadcast;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Xunit;
using BenchmarkFixtures = Chess.Web.Tests.Benchmark.Fixtures;
using GamesFixtures = Chess.Web.Tests.Games.Fixtures;
using TournamentFixtures = Chess.Web.Tests.Tournaments.Fixtures;

namespace Chess.Web.Tests.Components;

/// <summary>
/// The API goes away: the resilience handler's circuit opens (<see cref="BrokenCircuitException"/>) or a call times out
/// (a <see cref="TaskCanceledException"/> the page did not ask for). Neither is an <see cref="HttpRequestException"/>. A
/// loader that caught only that one ended the Blazor circuit, and a polling loop that ended on any cancellation stopped for good.
/// Every page below shows a plain message instead, keeps what it had, and goes on asking.
/// </summary>
public class OutageResilienceTests
{
    public enum Outage
    {
        OpenCircuit,
        Timeout,
    }

    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(40);

    private static Exception Failure(Outage outage) => outage == Outage.OpenCircuit
        ? new BrokenCircuitException("The circuit is now open and is not allowing calls.")
        : new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
            new TimeoutException());

    private static string Plain(Outage outage) => outage == Outage.OpenCircuit ? "not answering right now" : "took too long";

    private static void AssertPlain(string text, Outage outage)
    {
        Assert.Contains(Plain(outage), text);
        Assert.DoesNotContain("circuit is now open", text);
        Assert.DoesNotContain("HttpClient.Timeout", text);
    }

    // ---- Polling loops ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void TournamentsPageKeepsTheListShowsTheOutageAndKeepsPolling(Outage outage)
    {
        var api = new Api()
            .Route("GET /api/models", TournamentFixtures.Models)
            .Route("GET /api/tournaments", TournamentFixtures.Tournaments(TournamentFixtures.RunningTournament, TournamentFixtures.FinishedTournament))
            .Route("GET /api/cost/inputs", "{}", HttpStatusCode.NotFound);
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);

        var cut = ctx.Render<TournamentsPage>(p => p.Add(page => page.PollInterval, Fast));
        cut.WaitForAssertion(() => Assert.Contains("Summer cup", cut.Markup));

        api.Down(Failure(outage));
        cut.WaitForAssertion(() => AssertPlain(cut.Find(".status-error").TextContent, outage));
        Assert.Contains("Summer cup", cut.Markup); // the last list stays

        api.Up();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".status-error")));
        Assert.Contains("Summer cup", cut.Markup);
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void TournamentsPageShowsAnOutageAtFirstLoadAndRecoversOnItsOwn(Outage outage)
    {
        var api = new Api()
            .Route("GET /api/models", TournamentFixtures.Models)
            .Route("GET /api/tournaments", TournamentFixtures.Tournaments(TournamentFixtures.FinishedTournament))
            .Route("GET /api/cost/inputs", "{}", HttpStatusCode.NotFound);
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);

        var cut = ctx.Render<TournamentsPage>(p => p.Add(page => page.PollInterval, Fast));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the tournaments:", cut.Markup));
        AssertPlain(cut.Markup, outage);
        Assert.DoesNotContain("Loading tournaments", cut.Markup);

        api.Up();
        cut.WaitForAssertion(() => Assert.Contains("Summer cup", cut.Markup));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void TournamentDetailPageStaysOnItsErrorAndLoadsWhenTheApiComesBack(Outage outage)
    {
        var api = new Api()
            .Route($"GET /api/tournaments/{TournamentFixtures.RunningId}", TournamentFixtures.RunningTournament)
            .Route("GET /api/games?page=1&pageSize=200", TournamentFixtures.Page([], 0))
            .Route($"GET /api/ratings?tournamentId={TournamentFixtures.RunningId}", """{"models":[],"whiteAdvantage":0.5,"games":0,"moveQualityAvailable":false}""");
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);

        var cut = ctx.Render<TournamentDetailPage>(p => p
            .Add(page => page.Id, Guid.Parse(TournamentFixtures.RunningId))
            .Add(page => page.PollInterval, Fast));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the tournament:", cut.Markup));
        AssertPlain(cut.Markup, outage);

        // The first load failed and the tournament never loaded: the page must still be polling.
        api.Up();
        cut.WaitForAssertion(() => Assert.Contains("Spring cup", cut.Find("h1").TextContent));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void TournamentDetailPageKeepsItsFiguresWhenARefreshFails(Outage outage)
    {
        var api = new Api()
            .Route($"GET /api/tournaments/{TournamentFixtures.RunningId}", TournamentFixtures.RunningTournament)
            .Route("GET /api/games?page=1&pageSize=200", TournamentFixtures.Page([], 0))
            .Route($"GET /api/ratings?tournamentId={TournamentFixtures.RunningId}", """{"models":[],"whiteAdvantage":0.5,"games":0,"moveQualityAvailable":false}""");
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);
        var cut = ctx.Render<TournamentDetailPage>(p => p
            .Add(page => page.Id, Guid.Parse(TournamentFixtures.RunningId))
            .Add(page => page.PollInterval, Fast));
        cut.WaitForAssertion(() => Assert.Contains("Spring cup", cut.Find("h1").TextContent));

        api.Down(Failure(outage));
        cut.WaitForAssertion(() => AssertPlain(cut.Find("[data-testid='tournament-refresh-error']").TextContent, outage));
        Assert.Contains("Spring cup", cut.Find("h1").TextContent);

        api.Up();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='tournament-refresh-error']")));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void BenchmarkPageKeepsTheRunsShowsTheOutageAndKeepsPolling(Outage outage)
    {
        var api = new Api()
            .Route("GET /api/models", BenchmarkFixtures.Models)
            .Route("GET /api/cost/inputs", BenchmarkFixtures.CostInputs)
            .Route("GET /api/benchmarks", BenchmarkFixtures.Benchmarks(BenchmarkFixtures.FinishedBenchmark));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        var cut = ctx.Render<BenchmarkPage>(p => p.Add(page => page.PollInterval, Fast));
        cut.WaitForAssertion(() => Assert.Contains("Summer check", cut.Markup));

        api.Down(Failure(outage));
        cut.WaitForAssertion(() => AssertPlain(cut.Markup, outage));
        Assert.Contains("Summer check", cut.Markup);

        api.Up();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Could not load the runs", cut.Markup));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void BenchmarkReportPageKeepsTheLastReportWhenOnePollFails(Outage outage)
    {
        var running = BenchmarkFixtures.Report.Replace("\"status\":\"finished\"", "\"status\":\"running\"", StringComparison.Ordinal);
        var api = new Api()
            .Route($"GET /api/benchmarks/{BenchmarkFixtures.RunningId}/report", running)
            .Route($"GET /api/benchmarks/{BenchmarkFixtures.RunningId}/report?format=md", BenchmarkFixtures.Markdown, mediaType: "text/markdown");
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        var cut = ctx.Render<BenchmarkReportPage>(p => p
            .Add(page => page.Id, Guid.Parse(BenchmarkFixtures.RunningId))
            .Add(page => page.PollInterval, Fast));
        cut.WaitForAssertion(() => Assert.Contains("Spring check", cut.Find("h1").TextContent));

        api.Down(Failure(outage));
        cut.WaitForAssertion(() => AssertPlain(cut.Find("[data-testid='report-refresh-error']").TextContent, outage));
        Assert.Contains("Spring check", cut.Find("h1").TextContent); // the report is not blanked
        Assert.NotEmpty(cut.FindAll("table[data-testid='benchmark-opponents']"));

        api.Up();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='report-refresh-error']")));
        Assert.Contains("Spring check", cut.Find("h1").TextContent);
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void BenchmarkReportPageStillPollsWhenTheFirstLoadFailed(Outage outage)
    {
        var api = new Api()
            .Route($"GET /api/benchmarks/{BenchmarkFixtures.RunningId}/report", BenchmarkFixtures.Report.Replace("\"status\":\"finished\"", "\"status\":\"running\"", StringComparison.Ordinal))
            .Route($"GET /api/benchmarks/{BenchmarkFixtures.RunningId}/report?format=md", BenchmarkFixtures.Markdown, mediaType: "text/markdown");
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));

        var cut = ctx.Render<BenchmarkReportPage>(p => p
            .Add(page => page.Id, Guid.Parse(BenchmarkFixtures.RunningId))
            .Add(page => page.PollInterval, Fast));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the report:", cut.Markup));
        AssertPlain(cut.Markup, outage);
        api.Up();
        cut.WaitForAssertion(() => Assert.Contains("Spring check", cut.Find("h1").TextContent));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void RequestsPageKeepsTheQueueAndTheRefreshLoopSurvivesAFailure(Outage outage)
    {
        var api = new Api().Route("GET /api/requests/queue",
            LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1)));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        var cut = ctx.Render<RequestsPage>(p => p.Add(page => page.PollInterval, Fast));
        cut.WaitForAssertion(() => Assert.Contains("Ana", cut.Find("[data-testid=requests-queued]").TextContent));

        api.Down(Failure(outage));
        cut.WaitForAssertion(() => AssertPlain(cut.Find("[data-testid='requests-refresh-error']").TextContent, outage));
        Assert.Contains("Ana", cut.Find("[data-testid=requests-queued]").TextContent);

        api.Up();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='requests-refresh-error']")));
        Assert.Contains("Ana", cut.Find("[data-testid=requests-queued]").TextContent);
    }

    // ---- Loaders ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void GameReplayPageShowsAPlainErrorInsteadOfEndingTheCircuit(Outage outage)
    {
        var api = new Api();
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));

        var cut = ctx.Render<GameReplayPage>(p => p.Add(page => page.Id, Guid.Parse(GamesFixtures.FinishedId)));

        cut.WaitForAssertion(() => AssertPlain(cut.Find("[data-testid='replay-error']").TextContent, outage));
        Assert.Contains("Could not load the game:", cut.Find("[data-testid='replay-error']").TextContent);
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void ResultsPageShowsAPlainErrorInsteadOfEndingTheCircuit(Outage outage)
    {
        var api = new Api();
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);

        var cut = ctx.Render<ResultsPage>();

        cut.WaitForAssertion(() => AssertPlain(cut.Find(".results-error").TextContent, outage));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void HeadToHeadPageShowsAPlainErrorInsteadOfEndingTheCircuit(Outage outage)
    {
        var api = new Api();
        api.Down(Failure(outage));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => AssertPlain(cut.Find(".status-error").TextContent, outage));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void PlayPageShowsAPlainErrorInsteadOfEndingTheCircuit(Outage outage)
    {
        var api = new Api();
        api.Down(Failure(outage));
        var client = Canned(api);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var cut = ctx.Render<PlayPage>();

        cut.WaitForAssertion(() => AssertPlain(cut.Markup, outage));
        Assert.Contains("Could not load the models:", cut.Markup);
    }

    // ---- GamesPage: busy state and a failed page ----------------------------------------------------------------

    [Fact]
    public void GamesPageShowsItIsLoadingAndDisablesThePagerWhileTheApiIsSlow()
    {
        var gate = new TaskCompletionSource();
        var api = new Api()
            .Route("GET /api/models", GamesFixtures.Models)
            .Route("GET /api/games?page=1&pageSize=20", GamesFixtures.Json(GamesFixtures.Page(
                [GamesFixtures.Summary("66666666-6666-6666-6666-666666666666", "gpt", "claude")], 25)))
            .Route("GET /api/games?page=2&pageSize=20", GamesFixtures.Json(GamesFixtures.Page(
                [GamesFixtures.Summary("77777777-7777-7777-7777-777777777777", "claude", "gpt")], 25, page: 2)), gate: gate.Task);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Contains("Page 1 of 2", cut.Markup));

        cut.Find("[data-testid=games-next]").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=games-loading]")));
        Assert.True(cut.Find("[data-testid=games-next]").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid=games-prev]").HasAttribute("disabled"));

        gate.SetResult();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=games-loading]")));
        Assert.Contains("Page 2 of 2", cut.Markup);
        Assert.Equal("/games/77777777-7777-7777-7777-777777777777", cut.Find("[data-testid=game-row] a").GetAttribute("href"));
    }

    [Theory]
    [InlineData(Outage.OpenCircuit)]
    [InlineData(Outage.Timeout)]
    public void GamesPageKeepsThePageOnScreenWhenTheNextOneFails(Outage outage)
    {
        var api = new Api()
            .Route("GET /api/models", GamesFixtures.Models)
            .Route("GET /api/games?page=1&pageSize=20", GamesFixtures.Json(GamesFixtures.Page(
                [GamesFixtures.Summary("66666666-6666-6666-6666-666666666666", "gpt", "claude")], 25)));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Contains("Page 1 of 2", cut.Markup));

        api.Down(Failure(outage));
        cut.Find("[data-testid=games-next]").Click();

        cut.WaitForAssertion(() => AssertPlain(cut.Find("[data-testid=games-error]").TextContent, outage));
        Assert.Contains("Page 1 of 2", cut.Markup); // the pager names the page that is on screen
        Assert.Single(cut.FindAll("[data-testid=game-row]"));
        Assert.False(cut.Find("[data-testid=games-next]").HasAttribute("disabled"));
    }

    // ---- The forms: the API's reason and a safe default ---------------------------------------------------------

    [Fact]
    public void TheTournamentFormShowsTheReasonTheApiGaveNotTheBareStatus()
    {
        var api = new Api()
            .Route("GET /api/models", TournamentFixtures.Models)
            .Route("GET /api/tournaments", TournamentFixtures.Tournaments())
            .Route("GET /api/cost/inputs", "{}", HttpStatusCode.NotFound)
            .Route("POST /api/tournaments", """{"title":"Bad Request","status":400,"detail":"Max concurrency must be between 1 and 1 (Arena:MaxConcurrentGames)."}""",
                HttpStatusCode.BadRequest);
        using var ctx = new BunitContext();
        WireWithRatings(ctx, api);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#tournament-name").Input("Cup");
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        cut.Find("#start-tournament").Click();

        cut.WaitForAssertion(() => Assert.Contains("Max concurrency must be between 1 and 1", cut.Markup));
        Assert.DoesNotContain("400 (Bad Request)", cut.Markup);
    }

    [Fact]
    public void TheFormsStartOnAConcurrencyEveryApiAccepts()
    {
        Assert.Equal(1, TournamentFormRules.DefaultConcurrency);
        Assert.Empty(TournamentFormRules.Problems("Cup", ["a", "b"], 2, TournamentFormRules.DefaultConcurrency));
    }

    // ---- Support ------------------------------------------------------------------------------------------------

    /// <summary>The API client and the named client the Results adapter uses, both answering from <paramref name="api"/>.</summary>
    private static void WireWithRatings(BunitContext ctx, Api api)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned(api)));
        ctx.Services.AddHttpClient(TournamentRatingsClient.ClientName, client => client.BaseAddress = new Uri("http://api"))
            .ConfigurePrimaryHttpMessageHandler(() => api.Handler);
    }

    private static HttpClient Canned(Api api) => new(api.Handler) { BaseAddress = new Uri("http://api") };

    /// <summary>
    /// A fake API: answers its routes ("METHOD /path?query", anything else is a 404), and while it is <see cref="Down"/>
    /// every request throws the given exception, as the resilience handler does.
    /// </summary>
    private sealed class Api
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status, string MediaType, Task? Gate)> _routes = [];
        private volatile Exception? _failure;

        public Api()
        {
            Handler = new Scripted(this);
        }

        public HttpMessageHandler Handler { get; }

        public Api Route(string key, string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json", Task? gate = null)
        {
            _routes[key] = (body, status, mediaType, gate);
            return this;
        }

        public void Down(Exception failure) => _failure = failure;

        public void Up() => _failure = null;

        private sealed class Scripted(Api api) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (api._failure is { } failure)
                {
                    throw failure;
                }

                if (!api._routes.TryGetValue($"{request.Method} {request.RequestUri!.PathAndQuery}", out var route))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                }

                if (route.Gate is not null)
                {
                    await route.Gate;
                }

                return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, route.MediaType) };
            }
        }
    }
}
