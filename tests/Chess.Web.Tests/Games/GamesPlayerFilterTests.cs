using Bunit;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Games;

/// <summary>The games filter lists the rated players (model, effort and style), and its state lives in the address.</summary>
public class GamesPlayerFilterTests
{
    private const string FirstId = "66666666-6666-6666-6666-666666666666";

    private const string Ratings = """
        {"models":[
          {"model":"gpt@high","rating":1650,"ciLow":1600,"ciHigh":1700,"games":234,"score":0.7,"legality":0.99,"moveQuality":null,"quality":0.8,"arenaScore":61,"provisional":false},
          {"model":"claude~think","rating":1500,"ciLow":1450,"ciHigh":1550,"games":120,"score":0.5,"legality":0.97,"moveQuality":null,"quality":0.7,"arenaScore":50,"provisional":false},
          {"model":"gpt","rating":1400,"ciLow":1350,"ciHigh":1450,"games":10,"score":0.3,"legality":0.9,"moveQuality":null,"quality":0.5,"arenaScore":40,"provisional":true}
        ],"whiteAdvantage":10,"games":364,"moveQualityAvailable":false}
        """;

    private const string Styles = """
        [{"id":"classic","displayName":"Classic","description":"d","perMoveConversation":false,"version":"1"},
         {"id":"think","displayName":"Think","description":"d","perMoveConversation":false,"version":"1"}]
        """;

    private static FakeHandler Server(params (string Route, string Json)[] extra)
    {
        var routes = new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/ratings"] = Reply.Json(Ratings),
            ["GET /api/prompt-styles"] = Reply.Json(Styles),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page([Fixtures.Summary(FirstId, "gpt@high", "claude~think")], 1))),
        };
        foreach (var (route, json) in extra)
        {
            routes[route] = Reply.Json(json);
        }

        return Routes.Answer(routes);
    }

    [Fact]
    public void ThePickerListsRatedPlayersWithTheirEffortAndStyleThenTheCatalogueModelsWithoutARating()
    {
        using var ctx = new BunitContext();
        Wire.Api(ctx, Server());

        var cut = ctx.Render<GamesPage>();

        cut.WaitForAssertion(() => Assert.Equal(1 + 4, cut.FindAll("[data-testid=games-filter-model] option").Count));
        var options = cut.FindAll("[data-testid=games-filter-model] option")
            .Select(option => (option.GetAttribute("value"), option.TextContent)).ToArray();
        Assert.Equal(
            [("", "Any player"), ("gpt@high", "GPT @ high"), ("claude~think", "Claude · Think"), ("gpt", "GPT"), ("claude", "Claude")],
            options);
    }

    [Fact]
    public void ChoosingAPlayerSendsItsIdAndWritesItIntoTheAddress()
    {
        var handler = Server(("GET /api/games?page=1&pageSize=20&model=gpt%40high", Fixtures.Json(Fixtures.Page([Fixtures.Summary(FirstId, "gpt@high", "claude~think")], 1))));
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=game-row]")));

        cut.Find("[data-testid=games-filter-model]").Change("gpt@high");

        cut.WaitForAssertion(() => Assert.Contains(handler.Requests, r => r.Target == "/api/games?page=1&pageSize=20&model=gpt%40high"));
        var uri = ctx.Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains("model=gpt%40high", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("page=", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressWithFiltersAndAPageOpensOnThem()
    {
        var handler = Server(("GET /api/games?page=2&pageSize=20&model=gpt%40high&status=finished",
            Fixtures.Json(Fixtures.Page([Fixtures.Summary(FirstId, "gpt@high", "claude~think")], 45, page: 2))));
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/games?model=gpt%40high&status=finished&page=2");

        var cut = ctx.Render<GamesPage>();

        cut.WaitForAssertion(() => Assert.Contains("Page 2 of 3", cut.Find("[data-testid=games-page]").TextContent));
        Assert.Equal("gpt@high", cut.Find("[data-testid=games-filter-model]").GetAttribute("value"));
        Assert.Equal("finished", cut.Find("[data-testid=games-filter-status]").GetAttribute("value"));
    }

    [Fact]
    public void NextWritesThePageIntoTheAddressAndBackToPageOneRemovesIt()
    {
        var handler = Server(("GET /api/games?page=2&pageSize=20",
            Fixtures.Json(Fixtures.Page([Fixtures.Summary(FirstId, "gpt@high", "claude~think")], 25, page: 2))));
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=game-row]")));
        // One game on the first page of a 25-game archive: the pager is not enough to turn the page, so the address does.

        navigation.NavigateTo("/games?page=2");

        cut.WaitForAssertion(() => Assert.Contains("Page 2 of 2", cut.Find("[data-testid=games-page]").TextContent));
        cut.Find("[data-testid=games-prev]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Page 1 of", cut.Find("[data-testid=games-page]").TextContent));
        Assert.DoesNotContain("page=", navigation.Uri, StringComparison.Ordinal);
    }
}
