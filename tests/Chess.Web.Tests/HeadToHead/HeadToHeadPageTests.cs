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

    // One game: every bootstrap resample is the same, so the interval is a point well away from one half.
    private const string GptVsClaudeOneGame =
        """{"a":"gpt","b":"claude","expectedScore":0.7,"qualityShare":0.7,"rqc":0.7,"rqcLow":0.7,"rqcHigh":0.7,"eloEquivalent":147,"headToHeadGames":1,"headToHeadScore":1}""";

    private const string Ratings =
        """
        {"models":[
          {"model":"gpt","rating":120,"ciLow":100,"ciHigh":140,"games":30,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":66,"provisional":false},
          {"model":"claude","rating":90,"ciLow":70,"ciHigh":110,"games":30,"score":0.3,"legality":0.95,"moveQuality":null,"quality":0.95,"arenaScore":34,"provisional":false}],
         "whiteAdvantage":12,"games":60,"moveQualityAvailable":false}
        """;

    private const string RatingsWithRandom =
        """
        {"models":[
          {"model":"gpt","rating":120,"ciLow":100,"ciHigh":140,"games":30,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":66,"provisional":false},
          {"model":"claude","rating":90,"ciLow":70,"ciHigh":110,"games":30,"score":0.3,"legality":0.95,"moveQuality":null,"quality":0.95,"arenaScore":34,"provisional":false},
          {"model":"random","rating":10,"ciLow":0,"ciHigh":20,"games":30,"score":0.1,"legality":1,"moveQuality":null,"quality":1,"arenaScore":10,"provisional":false}],
         "whiteAdvantage":12,"games":90,"moveQualityAvailable":false}
        """;

    private const string RatingsWithStyledPlayers =
        """
        {"models":[
          {"model":"gpt@high","rating":120,"ciLow":100,"ciHigh":140,"games":234,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":66,"provisional":false},
          {"model":"gpt~think","rating":100,"ciLow":80,"ciHigh":120,"games":120,"score":0.5,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":50,"provisional":false},
          {"model":"claude","rating":90,"ciLow":70,"ciHigh":110,"games":30,"score":0.3,"legality":0.95,"moveQuality":null,"quality":0.95,"arenaScore":34,"provisional":false}],
         "whiteAdvantage":12,"games":384,"moveQualityAvailable":false}
        """;

    private const string StyledPair =
        """{"a":"gpt@high","b":"gpt~think","expectedScore":0.55,"qualityShare":0.5,"rqc":0.56,"rqcLow":0.5,"rqcHigh":0.62,"eloEquivalent":20,"headToHeadGames":0,"headToHeadScore":null}""";

    private const string RatingsWithProvisionalClaude =
        """
        {"models":[
          {"model":"gpt","rating":120,"ciLow":100,"ciHigh":140,"games":30,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.99,"arenaScore":66,"provisional":false},
          {"model":"claude","rating":90,"ciLow":40,"ciHigh":140,"games":6,"score":0.3,"legality":0.95,"moveQuality":null,"quality":0.95,"arenaScore":34,"provisional":true}],
         "whiteAdvantage":12,"games":36,"moveQualityAvailable":false}
        """;

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
            ["/api/ratings"] = Ok(Ratings),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games(PairGame, OtherGame)),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));
        Assert.Contains("0.62 for gpt", cut.Find(".h2h-hero").TextContent);
        Assert.Contains("(95 % interval 0.55 – 0.69)", cut.Markup);
        Assert.Contains("+82.4 Elo for gpt against claude", cut.Markup);
        Assert.Contains("2 direct games; gpt scored 0.75 per game.", cut.Markup);
        Assert.DoesNotContain("Not yet significant", cut.Markup);
        // The pickers are filled from the ratings first (players, not catalogue models), then the pair is read.
        Assert.Equal(new[]
            {
                "/api/ratings", "/api/models", "/api/prompt-styles",
                "/api/ratings/pairwise?a=gpt&b=claude", "/api/ratings", "/api/games?page=1&pageSize=100&model=gpt",
            },
            handler.Requests.Select(r => r.Target).ToArray());

        var rows = cut.FindAll("table.h2h-games tbody tr");
        var row = Assert.Single(rows);
        Assert.Contains("gpt", row.Children[1].TextContent);
        Assert.Contains("claude", row.Children[2].TextContent);
        Assert.Equal("1-0", row.Children[3].TextContent);
        Assert.Equal("1", row.Children[5].TextContent);
        Assert.Equal("/games/11111111-1111-1111-1111-111111111111", row.Children[6].QuerySelector("a")!.GetAttribute("href"));

        // The RQC is the hero number of the reading card, and the direct games sit in their own card, the table in a .table-wrap.
        Assert.Equal("0.62", cut.Find(".h2h-rqc").TextContent);
        Assert.Contains("card", cut.Find("section[aria-labelledby='h2h-games']").ClassName);
        Assert.NotNull(cut.Find(".table-wrap > table.h2h-games"));
    }

    [Fact]
    public void SaysTooFewGamesInsteadOfSignificantWhenAModelIsProvisional()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/ratings"] = Ok(RatingsWithProvisionalClaude),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        // The interval (0.55 – 0.69) excludes one half, yet the reading must not call it significant.
        cut.WaitForAssertion(() => Assert.Contains(
            "gpt is slightly better than claude. Not yet significant: too few games (need 20 rated games per model).",
            cut.Markup));
        Assert.Contains("0.62 for gpt", cut.Find(".h2h-hero").TextContent);
    }

    [Fact]
    public void ASingleGamePairHasNoIntervalToClaimALeadFrom()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaudeOneGame),
            ["/api/ratings"] = Ok(Ratings),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("Not significant: the 95 % interval has no width.", cut.Markup));
        Assert.DoesNotContain("Not yet significant: too few games", cut.Markup);
    }

    [Fact]
    public void LeavesTheDirectGamesOutWhenTheArchiveDoesNotAnswer()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/ratings"] = Ok(Ratings),
            ["/api/games?page=1&pageSize=100&model=gpt"] = Fail(HttpStatusCode.NotFound),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("gpt is slightly better than claude.", cut.Markup));
        Assert.Empty(cut.FindAll("table.h2h-games"));
        Assert.Empty(cut.FindAll("#h2h-games"));
        Assert.Contains("0.62 for gpt", cut.Find(".h2h-hero").TextContent);
    }

    [Fact]
    public void ChangingAPickerLoadsTheNewPair()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings/pairwise?a=gpt&b=claude"] = Ok(GptVsClaude),
            ["/api/ratings/pairwise?a=gpt&b=random"] = Ok(GptVsRandom),
            ["/api/ratings"] = Ok(RatingsWithRandom),
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
            ["/api/ratings"] = Ok(Ratings),
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
        // The ratings are asked for the pickers; no pair is read.
        Assert.DoesNotContain(handler.Requests, r => r.Target.StartsWith("/api/ratings/pairwise", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePickersListRatedPlayersAndTheDefaultPairIsTheTopTwo()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings"] = Ok(RatingsWithStyledPlayers),
            ["/api/prompt-styles"] = Ok("""[{"id":"classic","displayName":"Classic","description":"d","perMoveConversation":false,"version":"1"},{"id":"think","displayName":"Think","description":"d","perMoveConversation":false,"version":"1"}]"""),
            ["/api/ratings/pairwise?a=gpt%40high&b=gpt~think"] = Ok(StyledPair),
            ["/api/games?page=1&pageSize=100&model=gpt%40high"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains("0.56 for gpt@high", cut.Find(".h2h-hero").TextContent));
        var options = cut.FindAll("#model-a option").Select(o => (o.GetAttribute("value"), o.TextContent)).ToArray();
        Assert.Equal([("gpt@high", "GPT @ high"), ("gpt~think", "GPT · Think"), ("claude", "Claude")], options);
        Assert.Equal("gpt@high", cut.Find("#model-a").GetAttribute("value"));
        Assert.Equal("gpt~think", cut.Find("#model-b").GetAttribute("value"));
    }

    [Fact]
    public void AnAddressWithAPairOpensOnItAndPickingWritesTheNewPairIntoTheAddress()
    {
        var handler = Api(new Dictionary<string, Reply>
        {
            ["/api/models"] = Ok(Models),
            ["/api/ratings"] = Ok(RatingsWithRandom),
            ["/api/ratings/pairwise?a=claude&b=random"] = Ok(GptVsRandom.Replace("\"a\":\"gpt\"", "\"a\":\"claude\"")),
            ["/api/ratings/pairwise?a=claude&b=gpt"] = Ok(GptVsClaude.Replace("\"a\":\"gpt\",\"b\":\"claude\"", "\"a\":\"claude\",\"b\":\"gpt\"")),
            ["/api/games?page=1&pageSize=100&model=claude"] = Ok(Games()),
        });
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var navigation = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/head-to-head?a=claude&b=random");

        var cut = ctx.Render<HeadToHeadPage>();

        cut.WaitForAssertion(() => Assert.Contains(handler.Requests, r => r.Target == "/api/ratings/pairwise?a=claude&b=random"));
        Assert.Equal("claude", cut.Find("#model-a").GetAttribute("value"));
        Assert.Equal("random", cut.Find("#model-b").GetAttribute("value"));

        cut.Find("#model-b").Change("gpt");

        cut.WaitForAssertion(() => Assert.Contains(handler.Requests, r => r.Target == "/api/ratings/pairwise?a=claude&b=gpt"));
        Assert.Contains("a=claude", navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("b=gpt", navigation.Uri, StringComparison.Ordinal);
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
