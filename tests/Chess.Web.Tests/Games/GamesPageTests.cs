using System.Net;
using Bunit;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Games;

public class GamesPageTests
{
    private const string FirstId = "66666666-6666-6666-6666-666666666666";
    private const string SecondId = "77777777-7777-7777-7777-777777777777";

    [Fact]
    public void ListsEachGameOnItsOwnRowLinkedToItsReplay()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
            [
                Fixtures.Summary(FirstId, "gpt", "claude"),
                Fixtures.Summary(SecondId, "claude", "gpt", "running", "*", null, 0),
            ], 2))),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<GamesPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=games-table]")));
        var rows = cut.FindAll("[data-testid=game-row]");
        Assert.Equal(2, rows.Count);
        Assert.Equal($"/games/{FirstId}", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("gpt", rows[0].Children[1].TextContent);
        Assert.Equal("claude", rows[0].Children[2].TextContent);
        Assert.Equal("1-0", rows[0].Children[3].TextContent);
        Assert.Equal("checkmate", rows[0].Children[4].TextContent);
        Assert.Equal("Finished", rows[0].Children[6].TextContent);
        Assert.Equal("Running", rows[1].Children[6].TextContent);
        Assert.Equal("—", rows[1].Children[4].TextContent);
        Assert.Contains("Page 1 of 1 · 2 games", cut.Find("[data-testid=games-page]").TextContent);
    }

    [Fact]
    public void PagingMovesThroughTheArchiveAndStopsAtBothEnds()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(FirstId, "gpt", "claude")], 25))),
            ["GET /api/games?page=2&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(SecondId, "claude", "gpt")], 25, page: 2))),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Contains("Page 1 of 2", cut.Markup));
        Assert.True(cut.Find("[data-testid=games-prev]").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid=games-next]").HasAttribute("disabled"));

        cut.Find("[data-testid=games-next]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Page 2 of 2", cut.Markup));
        Assert.Equal("/api/games?page=2&pageSize=20", handler.Requests[^1].Target);
        Assert.False(cut.Find("[data-testid=games-prev]").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid=games-next]").HasAttribute("disabled"));
    }

    [Fact]
    public void ChoosingAModelAsksTheApiForThatModelAndStartsAgainOnPageOne()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(FirstId, "gpt", "claude")], 25))),
            ["GET /api/games?page=2&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(SecondId, "claude", "gpt")], 25, page: 2))),
            ["GET /api/games?page=1&pageSize=20&model=gpt"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(FirstId, "gpt", "claude")], 1))),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Contains("Page 1 of 2", cut.Markup));
        cut.Find("[data-testid=games-next]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Page 2 of 2", cut.Markup));

        cut.Find("[data-testid=games-filter-model]").Change("gpt");

        cut.WaitForAssertion(() => Assert.Contains("Page 1 of 1", cut.Markup));
        Assert.Equal("/api/games?page=1&pageSize=20&model=gpt", handler.Requests[^1].Target);
    }

    [Fact]
    public void ChoosingAStatusSendsItAndAnEmptyResultSaysNoGamesMatch()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page(
                [Fixtures.Summary(FirstId, "gpt", "claude")], 1))),
            ["GET /api/games?page=1&pageSize=20&status=queued"] = Reply.Json(Fixtures.Json(Fixtures.Page([], 0))),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<GamesPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=game-row]")));

        cut.Find("[data-testid=games-filter-status]").Change("queued");

        cut.WaitForAssertion(() => Assert.Contains("No games match these filters.", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=games-table]"));
        Assert.Equal("/api/games?page=1&pageSize=20&status=queued", handler.Requests[^1].Target);
    }

    [Fact]
    public void AnEmptyArchiveSaysNoGamesYetAndPointsAtTheLivePage()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json(Fixtures.Json(Fixtures.Page([], 0))),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<GamesPage>();

        cut.WaitForAssertion(() => Assert.Contains("No games yet. Start one on the Live game page.", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=games-table]"));
    }

    [Fact]
    public void AFailedListSaysSoInsteadOfShowingLoadingForever()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            ["GET /api/games?page=1&pageSize=20"] = Reply.Json("{}", HttpStatusCode.InternalServerError),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<GamesPage>();

        cut.WaitForAssertion(() => Assert.Contains("Could not load the games", cut.Markup));
        Assert.DoesNotContain("Loading games", cut.Markup);
    }
}
