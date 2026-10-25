using System.Net;
using Bunit;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Chess.Web.Tests.Games;

public class GameReplayPageTests
{
    private const string PgnPrefix = "data:application/x-chess-pgn;charset=utf-8,";

    /// <summary>
    /// A finished game of two moves: 1. e4 e5. White's e4 took two attempts (one illegal reply); Black's e5 was analysed
    /// only for the centipawn loss of White, so it has no analysis.
    /// </summary>
    private static FakeHandler FinishedGame() => Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
    {
        ["GET /api/models"] = Reply.Json(Fixtures.Models),
        [$"GET /api/games/{Fixtures.FinishedId}"] = Reply.Json(Fixtures.Json(Fixtures.Detail(
            Fixtures.Summary(Fixtures.FinishedId, "gpt", "claude"),
            Fixtures.StartFen,
            Fixtures.AfterE4E5,
            Fixtures.Move(1, "white", "e4", Fixtures.AfterE4, attempts: 2, latencyMs: 1234, centipawnLoss: 35, bestSan: "d4"),
            Fixtures.Move(2, "black", "e5", Fixtures.AfterE4E5, latencyMs: 800)))),
        [$"GET /api/games/{Fixtures.FinishedId}/pgn"] = Reply.Pgn(Fixtures.Pgn),
        [$"GET /api/games/{Fixtures.FinishedId}/usage"] = Reply.Json(Fixtures.Json(Fixtures.Usage(Fixtures.FinishedId, 1.5))),
        [$"GET /api/attempts?gameId={Fixtures.FinishedId}&page=1"] = Reply.Json(Fixtures.Json(Fixtures.Page(
        [
            Fixtures.Attempt(Fixtures.FinishedId, 1, 1, "gpt", legal: false, rejectionKind: "illegal", reasoning: "Thinking about e5"),
            Fixtures.Attempt(Fixtures.FinishedId, 1, 2, "gpt", legal: true, san: "e4"),
            Fixtures.Attempt(Fixtures.FinishedId, 2, 1, "claude", legal: true, san: "e5"),
        ], 3))),
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
    public void TheStepButtonsFormOneGroupAndTheBoardAndMovesSitInCards()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='replay-board']")));
        Assert.Equal(4, cut.FindAll(".replay-buttons[role=group] button").Count);
        Assert.True(cut.Find("[data-testid='replay-next']").ClassList.Contains("btn"));
        Assert.NotNull(cut.Find("[data-testid='replay-board']").Closest(".card"));
        Assert.NotNull(cut.Find("[data-testid='move-list']").Closest(".card"));
        Assert.True(cut.Find("[data-testid='move-details']").ClassList.Contains("card"));
    }

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
    public void TheMoveTableIsTheLiveGamesOneAndPicksAMoveByItsCell()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));
        Assert.Equal(2, cut.FindAll("[data-testid='move-cell']").Count);

        cut.FindAll("[data-testid='move-cell']")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Ply 2 of 2", cut.Markup));
        var cells = cut.FindAll("[data-testid='move-cell']");
        Assert.Equal("true", cells[1].GetAttribute("aria-pressed"));
        Assert.Equal("false", cells[0].GetAttribute("aria-pressed"));
    }

    [Fact]
    public void PickingTheMoveOnTheBoardAgainKeepsThePly()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 2", cut.Markup));

        cut.FindAll("[data-testid='move-cell']")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("Ply 1 of 2", cut.Markup));

        cut.FindAll("[data-testid='move-cell']")[0].Click();

        Assert.Contains("Ply 1 of 2", cut.Markup);
    }

    [Fact]
    public void TheEngineLogListsEveryAttemptWithTheModelNameAndHighlightsTheMoveOnTheBoard()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid='engine-entry']").Count));
        Assert.Empty(cut.FindAll(".engine-entry-selected"));
        // Newest first: Claude's ply 2, then GPT's two attempts at ply 1.
        var models = cut.FindAll("[data-testid='engine-entry'] .engine-model").Select(model => model.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Claude", "GPT", "GPT" }, models);

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".engine-entry-selected").Count));
        Assert.All(cut.FindAll(".engine-entry-selected"), entry => Assert.Equal("1", entry.GetAttribute("data-ply")));
        // Newest first: the legal second attempt above the illegal first one.
        var verdicts = cut.FindAll(".engine-entry-selected [data-testid='verdict']").Select(verdict => verdict.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "legal", "illegal" }, verdicts);

        Click(cut, "replay-next");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".engine-entry-selected")));
        Assert.Equal("2", cut.Find(".engine-entry-selected").GetAttribute("data-ply"));
    }

    [Fact]
    public void TheSummaryOfAFinishedGameShowsEachSidesUsageAndCost()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, FinishedGame(), Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='usage-table']")));
        Assert.Equal("$1.50", cut.Find("[data-testid='usage-white'] [data-testid='usage-usd']").TextContent);
        Assert.Equal("reported", cut.Find("[data-testid='usage-white']").GetAttribute("data-basis"));
        Assert.Equal("—", cut.Find("[data-testid='usage-black'] [data-testid='usage-usd']").TextContent);
        Assert.Equal("unknown", cut.Find("[data-testid='usage-black']").GetAttribute("data-basis"));
        Assert.Equal("$1.50", cut.Find("[data-testid='usage-total-usd']").TextContent);
        Assert.Contains("AI credits × $0.0100 per AI credit", cut.Find("[data-testid='usage-rate']").TextContent);
    }

    [Fact]
    public void ARunningGameHasNoSummaryAndAsksNoUsageAndNoPgn()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            [$"GET /api/games/{Fixtures.RunningId}"] = Reply.Json(Fixtures.Json(Fixtures.Detail(
                Fixtures.Summary(Fixtures.RunningId, "gpt", "claude", "running", "*", null, 0),
                Fixtures.StartFen,
                Fixtures.StartFen))),
            [$"GET /api/attempts?gameId={Fixtures.RunningId}&page=1"] = Reply.Json(Fixtures.Json(Fixtures.Page([], 0))),
        });
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler, Fixtures.RunningId);

        cut.WaitForAssertion(() => Assert.Contains("Ply 0 of 0", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid='pgn-download']"));
        Assert.Contains("available once the game has finished", cut.Find("[data-testid='pgn-unavailable']").TextContent);
        Assert.Contains("This game has not finished", cut.Markup);
        Assert.Contains("No moves yet.", cut.Markup);
        Assert.Contains("The summary is available once the game has finished.", cut.Find("[data-testid='usage-pending']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='usage-table']"));
        Assert.Contains("No attempts yet.", cut.Find("[data-testid='log-empty']").TextContent);
        Assert.DoesNotContain(handler.Requests, request => request.Target.EndsWith("/pgn"));
        Assert.DoesNotContain(handler.Requests, request => request.Target.EndsWith("/usage"));
    }

    [Fact]
    public void AFailedEngineLogSaysSoAndTheBoardStillReplays()
    {
        var handler = Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["GET /api/models"] = Reply.Json(Fixtures.Models),
            [$"GET /api/games/{Fixtures.FinishedId}"] = Reply.Json(Fixtures.Json(Fixtures.Detail(
                Fixtures.Summary(Fixtures.FinishedId, "gpt", "claude"),
                Fixtures.StartFen,
                Fixtures.AfterE4E5,
                Fixtures.Move(1, "white", "e4", Fixtures.AfterE4, attempts: 1),
                Fixtures.Move(2, "black", "e5", Fixtures.AfterE4E5)))),
            [$"GET /api/games/{Fixtures.FinishedId}/pgn"] = Reply.Pgn(Fixtures.Pgn),
            [$"GET /api/games/{Fixtures.FinishedId}/usage"] = Reply.Json(Fixtures.Json(Fixtures.Usage(Fixtures.FinishedId, 1.5))),
            [$"GET /api/attempts?gameId={Fixtures.FinishedId}&page=1"] = Reply.Json("{}", HttpStatusCode.InternalServerError),
        });
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler, Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='attempts-error']")));
        Assert.Contains("Could not load the engine log", cut.Find("[data-testid='attempts-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='engine-entry']"));
        Assert.Single(cut.FindAll("[data-testid='replay-board']"));
        Assert.Single(cut.FindAll("[data-testid='usage-table']"));
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
    public void AnUnknownGameSaysSoAndShowsNoBoard()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes.Answer(new Dictionary<string, Func<HttpResponseMessage>>()), Fixtures.FinishedId);

        cut.WaitForAssertion(() => Assert.Contains("No game has this id.", cut.Find("[data-testid='replay-error']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='replay-board']"));
    }
}
