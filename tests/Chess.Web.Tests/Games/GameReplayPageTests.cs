using Bunit;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Chess.Web.Tests.Games;

public class GameReplayPageTests
{
    private const string PgnPrefix = "data:application/x-chess-pgn;charset=utf-8,";

    /// <summary>A finished game of two moves: 1. e4 e5 (the white move is analysed, the black one is not).</summary>
    private static FakeHandler FinishedGame() => Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
    {
        [$"GET /api/games/{Fixtures.FinishedId}"] = Reply.Json(Fixtures.Json(Fixtures.Detail(
            Fixtures.Summary(Fixtures.FinishedId, "gpt", "claude"),
            Fixtures.StartFen,
            Fixtures.AfterE4E5,
            Fixtures.Move(1, "white", "e4", Fixtures.AfterE4, attempts: 2, latencyMs: 1234, centipawnLoss: 35, bestSan: "d4"),
            Fixtures.Move(2, "black", "e5", Fixtures.AfterE4E5, latencyMs: 800)))),
        [$"GET /api/games/{Fixtures.FinishedId}/pgn"] = Reply.Pgn(Fixtures.Pgn),
    });

    private static IRenderedComponent<GameReplayPage> Render(BunitContext ctx, FakeHandler handler, string id)
    {
        Wire.Api(ctx, handler);
        return ctx.Render<GameReplayPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(id)));
    }

    private static void Click(IRenderedComponent<GameReplayPage> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").Click();

    private static void Press(IRenderedComponent<GameReplayPage> cut, string key) =>
        cut.Find("[data-testid='replay-board']").KeyDown(new KeyboardEventArgs { Key = key });

    [Fact]
    public void StepsForwardAndBackAndHighlightsTheMoveOnTheBoard()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));
        Assert.Empty(cut.FindAll("[data-highlight]"));

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Contains("Ply 1 of 2", cut.Markup));
        Assert.NotNull(cut.Find("[data-highlight='e2']"));
        Assert.NotNull(cut.Find("[data-highlight='e4']"));

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Contains("Ply 2 of 2", cut.Markup));
        Assert.NotNull(cut.Find("[data-highlight='e7']"));
        Assert.NotNull(cut.Find("[data-highlight='e5']"));
        Assert.True(cut.Find("[data-testid='replay-next']").HasAttribute("disabled"));

        Click(cut, "replay-prev");

        cut.WaitForAssertion(() => Assert.Contains("Ply 1 of 2", cut.Markup));
        Assert.Empty(cut.FindAll("[data-highlight='e7']"));
    }

    [Fact]
    public void FirstAndLastJumpToTheEndsAndTheButtonsThereAreDisabled()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));

        Click(cut, "replay-last");

        cut.WaitForAssertion(() => Assert.Contains("Ply 2 of 2", cut.Markup));
        Assert.True(cut.Find("[data-testid='replay-last']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='replay-first']").HasAttribute("disabled"));

        Click(cut, "replay-first");

        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));
        Assert.True(cut.Find("[data-testid='replay-first']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='replay-prev']").HasAttribute("disabled"));
    }

    [Fact]
    public void ArrowKeysAndHomeAndEndStepThroughTheMovesOnlyForKeysTheReplayUses()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));

        Press(cut, "ArrowRight");
        cut.WaitForAssertion(() => Assert.Contains("Ply 1 of 2", cut.Markup));

        Press(cut, "End");
        cut.WaitForAssertion(() => Assert.Contains("Ply 2 of 2", cut.Markup));

        Press(cut, "ArrowLeft");
        cut.WaitForAssertion(() => Assert.Contains("Ply 1 of 2", cut.Markup));

        Press(cut, "Home");
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));

        Press(cut, "a");
        Assert.Contains("Ply 0 of 2", cut.Markup);
    }

    [Fact]
    public void ShowsTheAttemptsLatencyCentipawnLossAndBestMoveOfTheMoveOnTheBoard()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));
        Assert.Contains("Start position", cut.Find("[data-testid='move-details']").TextContent);

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Contains("White played e4", cut.Markup));
        Assert.Equal("2", cut.Find("[data-testid='move-attempts']").TextContent);
        Assert.Equal("1.2 s", cut.Find("[data-testid='move-latency']").TextContent);
        Assert.Equal("120 in / 8 out", cut.Find("[data-testid='move-tokens']").TextContent);
        Assert.Equal("35 cp", cut.Find("[data-testid='move-cpl']").TextContent);
        Assert.Equal("d4", cut.Find("[data-testid='move-best']").TextContent);

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Contains("Black played e5", cut.Markup));
        Assert.Equal("1", cut.Find("[data-testid='move-attempts']").TextContent);
        Assert.Equal("800 ms", cut.Find("[data-testid='move-latency']").TextContent);
        Assert.Equal("not analysed", cut.Find("[data-testid='move-cpl']").TextContent);
        Assert.Equal("not analysed", cut.Find("[data-testid='move-best']").TextContent);
    }

    [Fact]
    public void ClickingAMoveInTheListJumpsToIt()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));

        cut.FindAll("[data-testid='move-select']")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Ply 2 of 2", cut.Markup));
        Assert.Equal("current", cut.FindAll("[data-testid='move-row']")[1].ClassName);
    }

    [Fact]
    public void DownloadsThePgnOfAFinishedGameUnderTheNameTheApiGivesIt()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='pgn-download']")));
        var link = cut.Find("[data-testid='pgn-download']");
        Assert.Equal($"game-{Fixtures.FinishedId}.pgn", link.GetAttribute("download"));
        var href = link.GetAttribute("href")!;
        Assert.StartsWith(PgnPrefix, href);
        Assert.Equal(Fixtures.Pgn, Uri.UnescapeDataString(href[PgnPrefix.Length..]));
    }

    [Fact]
    public void ARunningGameHasNoPgnToDownloadAndAsksNoPgnFromTheApi()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            [$"GET /api/games/{Fixtures.RunningId}"] = Reply.Json(Fixtures.Json(Fixtures.Detail(
                Fixtures.Summary(Fixtures.RunningId, "gpt", "claude", "running", "*", null, 0),
                Fixtures.StartFen,
                Fixtures.StartFen))),
        });
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler, Fixtures.RunningId);

        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 0", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid='pgn-download']"));
        Assert.Contains("available once the game has finished", cut.Find("[data-testid='pgn-unavailable']").TextContent);
        Assert.Contains("This game has not finished", cut.Markup);
        Assert.Contains("No moves yet.", cut.Markup);
        Assert.DoesNotContain(handler.Requests, request => request.Target.EndsWith("/pgn"));
    }

    [Fact]
    public void AnUnknownGameSaysSoAndShowsNoBoard()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>()), Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Contains("No game has this id.", cut.Find("[data-testid='replay-error']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='replay-board']"));
    }
}
