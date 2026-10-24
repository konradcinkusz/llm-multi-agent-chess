using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Results;

public class ResultsPageTests
{
    private static readonly string TournamentId = "11111111-2222-3333-4444-555555555555";

    private static readonly string[] SectionOrder =
        ["big-picture", "rating-history", "head-to-head", "breakdown", "move-quality", "strength-cost", "table"];

    private const string TwoModels =
        """
        {"models":[
          {"model":"alpha","rating":120,"ciLow":100,"ciHigh":140,"games":30,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":66,"provisional":false},
          {"model":"beta","rating":90,"ciLow":70,"ciHigh":110,"games":30,"score":0.3,"legality":0.95,"moveQuality":null,"quality":0.95,"arenaScore":34,"provisional":false}],
         "whiteAdvantage":12,"games":60,"moveQualityAvailable":false}
        """;

    private const string EmptyBoard = """{"models":[],"whiteAdvantage":0,"games":0,"moveQualityAvailable":false}""";

    private const string Matrix =
        """
        {"models":["alpha","beta"],"cells":[
          {"a":"alpha","b":"beta","expectedScore":0.6,"qualityShare":0.5,"rqc":0.6,"rqcLow":0.52,"rqcHigh":0.68,"eloEquivalent":70,"headToHeadGames":5,"headToHeadScore":0.6},
          {"a":"beta","b":"alpha","expectedScore":0.4,"qualityShare":0.5,"rqc":0.4,"rqcLow":0.32,"rqcHigh":0.48,"eloEquivalent":-70,"headToHeadGames":5,"headToHeadScore":0.4}]}
        """;

    private const string Breakdown =
        """
        [{"model":"alpha","games":30,"wins":20,"draws":2,"losses":8,"forfeitLosses":1,"moves":400,"firstAttemptLegalRate":0.98,"avgLatencyMs":1500,"avgInputTokens":null,"avgOutputTokens":null,"averageCentipawnLoss":null},
          {"model":"beta","games":30,"wins":8,"draws":2,"losses":20,"forfeitLosses":0,"moves":400,"firstAttemptLegalRate":0.9,"avgLatencyMs":800,"avgInputTokens":null,"avgOutputTokens":null,"averageCentipawnLoss":null}]
        """;

    private static string Pair(double low, double high) =>
        $$"""{"a":"alpha","b":"beta","expectedScore":0.6,"qualityShare":0.5,"rqc":0.6,"rqcLow":{{low}},"rqcHigh":{{high}},"eloEquivalent":70,"headToHeadGames":5,"headToHeadScore":0.6}""";

    [Fact]
    public void SectionsAppearInTheOrderOfSection6_1()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Respond(board: TwoModels, pair: Pair(0.52, 0.68)));

        cut.WaitForAssertion(() => Assert.Equal(SectionOrder,
            cut.FindAll("section[data-section]").Select(s => s.GetAttribute("data-section"))));
    }

    [Fact]
    public void TheHeadlineNamesTheTopModelAndItsRating()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Respond(board: TwoModels, pair: Pair(0.52, 0.68)));

        cut.WaitForAssertion(() => Assert.Contains("Best overall: alpha", cut.Find(".headline").TextContent));
        Assert.Contains("rating 120 ± 20", cut.Find(".headline").TextContent);
    }

    [Fact]
    public void SaysNotYetSignificantWhenTheIntervalAgainstSecondContainsOneHalf()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Respond(board: TwoModels, pair: Pair(0.45, 0.70)));

        cut.WaitForAssertion(() => Assert.Contains("Not yet significantly ahead of #2 (beta)", cut.Find(".verdict").TextContent));
    }

    [Fact]
    public void SaysSignificantWhenTheWholeIntervalIsAboveOneHalf()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Respond(board: TwoModels, pair: Pair(0.55, 0.82)));

        cut.WaitForAssertion(() => Assert.Contains("Significantly ahead of #2 (beta)", cut.Find(".verdict").TextContent));
        Assert.DoesNotContain("Not yet", cut.Find(".verdict").TextContent);
    }

    [Fact]
    public void WithNoRatedGamesItExplainsAndLinksToTournaments()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Respond(board: EmptyBoard, pair: Pair(0.5, 0.5)));

        cut.WaitForAssertion(() => Assert.Contains("No rated games yet", cut.Find(".results-empty").TextContent));
        Assert.Equal("/tournaments", cut.Find(".results-empty a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("section[data-section='big-picture']"));
    }

    [Fact]
    public void ChoosingATournamentScopesTheRatingAndBreakdownRequests()
    {
        var tournaments = $$"""[{"id":"{{TournamentId}}","name":"Spring cup","models":["alpha","beta"],"gamesPerPair":5,"status":"finished","gamesTotal":10,"gamesFinished":10,"createdAt":"2026-10-01T10:00:00Z","finishedAt":null}]""";
        using var ctx = new BunitContext();
        var handler = Respond(board: TwoModels, pair: Pair(0.52, 0.68), tournaments: tournaments);
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Contains("Best overall: alpha", cut.Find(".headline").TextContent));

        cut.Find("select").Change(TournamentId);

        cut.WaitForAssertion(() =>
        {
            var targets = handler.Requests.Select(r => r.Target).ToList();
            Assert.Contains($"/api/ratings?tournamentId={TournamentId}", targets);
            Assert.Contains($"/api/results/breakdown?tournamentId={TournamentId}", targets);
            Assert.Contains($"/api/ratings/matrix?tournamentId={TournamentId}", targets);
        });
    }

    [Fact]
    public void ShowsTheReasonWhenTheApiCannotBeReached()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, new FakeHandler(_ => Canned.Json("{}", HttpStatusCode.BadGateway)));

        cut.WaitForAssertion(() => Assert.Contains("Results unavailable", cut.Find(".results-error").TextContent));
    }

    private static FakeHandler Respond(string board, string pair, string tournaments = "[]")
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/tournaments"] = tournaments,
            ["/api/ratings"] = board,
            ["/api/ratings/matrix"] = Matrix,
            ["/api/ratings/pairwise"] = pair,
            ["/api/ratings/history"] = "[]",
            ["/api/results/breakdown"] = Breakdown,
        };
        return new FakeHandler(request =>
        {
            var path = request.Target.Split('?')[0];
            return bodies.TryGetValue(path, out var body)
                ? Canned.Json(body)
                : Canned.Json("{}", HttpStatusCode.NotFound);
        });
    }

    private static IRenderedComponent<ResultsPage> Render(BunitContext ctx, FakeHandler handler)
    {
        var client = Canned.Client(handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton<IHttpClientFactory>(new FixedClientFactory(client));
        return ctx.Render<ResultsPage>();
    }

    /// <summary>Hands the same configured client to every name, so the page's adapter and typed client share one handler.</summary>
    private sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
