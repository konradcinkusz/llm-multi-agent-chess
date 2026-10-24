using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.HeadToHead;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.HeadToHead;

public class HeadToHeadPageTests
{
    private const string Models = """
        [
          {"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true},
          {"id":"claude","displayName":"Claude","premiumMultiplier":1,"enabled":true},
          {"id":"random","displayName":"Random","premiumMultiplier":null,"enabled":true}
        ]
        """;

    private const string GptVsClaude =
        """{"a":"gpt","b":"claude","expectedScore":0.58,"qualityShare":0.52,"rqc":0.62,"rqcLow":0.55,"rqcHigh":0.69,"eloEquivalent":82.4,"headToHeadGames":2,"headToHeadScore":0.75}""";

    private const string GptVsRandom =
        """{"a":"gpt","b":"random","expectedScore":0.9,"qualityShare":0.8,"rqc":0.88,"rqcLow":0.84,"rqcHigh":0.92,"eloEquivalent":400.1,"headToHeadGames":0,"headToHeadScore":null}""";

    private static string Games(params string[] items) =>
        $$"""{"items":[{{string.Join(',', items)}}],"page":1,"pageSize":100,"total":{{items.Length}}}""";

    private const string PairGame =
        """{"id":"11111111-1111-1111-1111-111111111111","tournamentId":null,"white":{"kind":"llm","modelId":"gpt"},"black":{"kind":"llm","modelId":"claude"},"rated":true,"state":"finished","result":"1-0","termination":"checkmate","plyCount":41,"createdAt":"2026-10-24T10:00:00Z","finishedAt":"2026-10-24T10:20:00Z"}""";

    private const string OtherGame =
        """{"id":"22222222-2222-2222-2222-222222222222","tournamentId":null,"white":{"kind":"llm","modelId":"gpt"},"black":{"kind":"random","modelId":"random"},"rated":true,"state":"finished","result":"1-0","termination":"checkmate","plyCount":30,"createdAt":"2026-10-24T11:00:00Z","finishedAt":"2026-10-24T11:20:00Z"}""";

    [Fact]
    public void ReadsTheCoefficientOfTheFirstTwoModelsAndTheirDirectGames()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games(PairGame, OtherGame)),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));
        Assert.Contains("0.62 for gpt", cut.Markup);
        Assert.Contains("(95 % interval 0.55 – 0.69)", cut.Markup);
        Assert.Contains("+82.4 Elo for gpt against claude", cut.Markup);
        Assert.Contains("2 direct games; gpt scored 0.75 per game.", cut.Markup);
        Assert.Equal(new[] { "/api/models", "/api/ratings/pairwise?a=gpt&b=claude", "/api/games?page=1&pageSize=100&model=gpt" },
            handler.Requests.Select(r => r.Target).ToArray());

        var rows = cut.FindAll("table.h2h-games tbody tr");
        var row = Assert.Single(rows);
        Assert.Contains("gpt", row.Children[1].TextContent);
        Assert.Contains("claude", row.Children[2].TextContent);
        Assert.Equal("1-0", row.Children[3].TextContent);
        Assert.Equal("1", row.Children[5].TextContent);
        Assert.Equal("/games/11111111-1111-1111-1111-111111111111", row.Children[6].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void LeavesTheDirectGamesOutWhenTheArchiveDoesNotAnswer()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Fail(HttpStatusCode.NotFound),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));
        Assert.Empty(cut.FindAll("table.h2h-games"));
        Assert.Empty(cut.FindAll("#h2h-games"));
        Assert.Contains("0.62 for gpt", cut.Markup);
    }

    [Fact]
    public void ChangingAPickerLoadsTheNewPair()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/ratings/pairwise?a=gpt&b=random"] = Ok(GptVsRandom),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var cut = ctx.Render<HeadToHeadPage>();
        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));

        cut.Find("#model-b").Change("random");

        cut.WaitForAssertion(() => Assert.Contains("gpt is dominant over random.", cut.Markup));
        Assert.Contains("/api/ratings/pairwise?a=gpt&b=random", handler.Requests.Select(r => r.Target));
        Assert.Contains("No direct games yet.", cut.Markup);
    }

    [Fact]
    public void AsksForTwoDifferentModelsInsteadOfComparingAModelWithItself()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var cut = ctx.Render<HeadToHeadPage>();
        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));

        cut.Find("#model-b").Change("gpt");

        cut.WaitForAssertion(() => Assert.Contains("Choose two different models to compare.", cut.Markup));
        Assert.DoesNotContain("/api/ratings/pairwise?a=gpt&b=gpt", handler.Requests.Select(r => r.Target));
    }

    [Fact]
    public void SaysSoWhenAModelHasNoRatingYet()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Fail(HttpStatusCode.NotFound),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("One of these models has no rating data yet.", cut.Markup));
        Assert.DoesNotContain("Reading", cut.Markup);
    }

    [Fact]
    public void ShowsTheFailureInsteadOfBreakingWhenTheModelsCannotBeLoaded()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Fail(HttpStatusCode.BadGateway),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("Could not load the models:", cut.Markup));
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain("/api/ratings/pairwise", handler.Requests.Select(r => r.Target));
    }

    [Fact]
    public void ExplainsThatTwoModelsAreNeededWhenTheArenaHasOnlyOne()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok("""[{"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true}]"""),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("At least two models are needed to compare.", cut.Markup));
        Assert.DoesNotContain(handler.Requests, r => r.Target.StartsWith("/api/ratings", StringComparison.Ordinal));
    }

    // Each request gets a fresh response: the client disposes the one it read.
    private static FakeHandler Api(IReadOnlyDictionary<string, Reply> routes) =>
        new(request => routes.TryGetValue(request.Target, out var reply)
            ? Canned.Json(reply.Json, reply.Status)
            : Canned.Json("{}", HttpStatusCode.NotFound));

    private sealed record Reply(HttpStatusCode Status, string Json);

    private static Reply Ok(string json) => new(HttpStatusCode.OK, json);

    private static Reply Fail(HttpStatusCode status) => new(status, "{}");
}
