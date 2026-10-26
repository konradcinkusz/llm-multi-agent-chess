using Bunit;
using Microsoft.AspNetCore.Components;
using Chess.Web.Api;
using Chess.Web.Components.Shared.Game;
using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>The move table, player bar, engine log and usage summary of the live page (03 §10.4), rendered with bUnit.</summary>
public class MoveTableTests
{
    private const string Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void MovesAreNumberedInPairsAsWrittenDown()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p.Add(x => x.Moves, [Move(1, "e4"), Move(2, "e5"), Move(3, "Nf3")]));

        var rows = cut.FindAll("[data-testid=move-list] li");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "e4", "e5" }, rows[0].QuerySelectorAll("[data-testid=move-cell]").Select(cell => cell.TextContent.Trim()));
        Assert.Equal("1.", rows[0].QuerySelector(".move-number")!.TextContent);
        Assert.Equal("2.", rows[1].QuerySelector(".move-number")!.TextContent);
        Assert.Equal("Nf3", rows[1].QuerySelector("[data-testid=move-cell]")!.TextContent.Trim());
        Assert.Single(rows[1].QuerySelectorAll(".move-empty"));
    }

    [Fact]
    public void AMoveThatNeededRetriesCarriesACountAndTheLatestMoveIsMarked()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p.Add(x => x.Moves, [Move(1, "e4"), Move(2, "e5", attempts: 3)]));

        var retries = cut.Find("[data-testid=move-retries]");
        Assert.Equal("3 tries", retries.TextContent);
        Assert.Single(cut.FindAll("[data-testid=move-retries]"));
        var latest = cut.FindAll("[data-testid=move-cell]").Single(cell => cell.GetAttribute("data-ply") == "2");
        Assert.Equal("step", latest.GetAttribute("aria-current"));
        Assert.Contains("move-latest", latest.ClassList);
        Assert.Equal("Black e5, 3 attempts", cut.Find("[data-ply='2']").GetAttribute("aria-label"));
    }

    [Fact]
    public void ThePlyBeingDecidedIsAPendingCellInItsColumn()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p
            .Add(x => x.Moves, [Move(1, "e4")])
            .Add(x => x.PendingPly, 2));

        var pending = cut.Find("[data-testid=move-pending]");
        Assert.Equal("Black is thinking", pending.GetAttribute("aria-label"));
        Assert.Equal("…", pending.TextContent);
        Assert.Single(cut.FindAll("[data-testid=move-list] li"));
    }

    [Fact]
    public void PickingAMoveSelectsItAndPickingItAgainClearsIt()
    {
        using var ctx = new BunitContext();
        int? picked = -1;
        var cut = ctx.Render<MoveTable>(p => p
            .Add(x => x.Moves, [Move(1, "e4"), Move(2, "e5")])
            .Add(x => x.OnSelect, EventCallback.Factory.Create<int?>(this, ply => picked = ply)));

        cut.Find("[data-ply='2']").Click();
        Assert.Equal(2, picked);

        cut.Render(p => p.Add(x => x.SelectedPly, 2));
        Assert.Equal("true", cut.Find("[data-ply='2']").GetAttribute("aria-pressed"));
        cut.Find("[data-ply='2']").Click();
        Assert.Null(picked);
    }

    [Fact]
    public void AGameWithoutMovesSaysSo()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p.Add(x => x.Moves, []));

        Assert.Equal("No moves yet.", cut.Find("[data-testid=no-moves]").TextContent);
    }

    [Fact]
    public void TheLiveListPutsTheNewestMoveOnTopInABoxOfFixedHeight()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p
            .Add(x => x.Moves, [Move(1, "e4"), Move(2, "e5"), Move(3, "Nf3")])
            .Add(x => x.PendingPly, 4)
            .Add(x => x.NewestFirst, true));

        var rows = cut.FindAll("[data-testid=move-list] li");
        Assert.Equal(new[] { "2.", "1." }, rows.Select(row => row.QuerySelector(".move-number")!.TextContent));
        Assert.Equal("Nf3", rows[0].QuerySelector("[data-testid=move-cell]")!.TextContent.Trim());
        Assert.NotNull(rows[0].QuerySelector("[data-testid=move-pending]"));
        Assert.Contains("move-table-fixed", cut.Find(".move-table-scroll").ClassList);
    }

    [Fact]
    public void TheReplayListKeepsTheGameInOrder()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p.Add(x => x.Moves, [Move(1, "e4"), Move(2, "e5"), Move(3, "Nf3")]));

        Assert.Equal(new[] { "1.", "2." }, cut.FindAll("[data-testid=move-list] li").Select(row => row.QuerySelector(".move-number")!.TextContent));
        Assert.DoesNotContain("move-table-fixed", cut.Find(".move-table-scroll").ClassList);
    }

    [Fact]
    public void TheEmptyStateSitsInsideTheList()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MoveTable>(p => p.Add(x => x.Moves, []).Add(x => x.NewestFirst, true));

        Assert.NotNull(cut.Find("[data-testid=no-moves]").Closest(".move-table-scroll"));
    }

    private static MoveDto Move(int ply, string san, int attempts = 1) =>
        new(ply, ply % 2 == 1 ? "White" : "Black", san, Fen, attempts, 900, null, null, null, null);
}

/// <summary>Collapses the line breaks and indentation Razor leaves in text, so a test compares the words only.</summary>
internal static class TextWords
{
    public static string Words(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public class PlayerBarTests
{

    [Fact]
    public void TheSideToMoveIsMarkedAndTheOtherIsNot()
    {
        using var ctx = new BunitContext();

        var white = ctx.Render<PlayerBar>(p => p.Add(x => x.Color, "White").Add(x => x.Name, "GPT").Add(x => x.ToMove, true));
        var black = ctx.Render<PlayerBar>(p => p.Add(x => x.Color, "Black").Add(x => x.Name, "Claude").Add(x => x.ToMove, false));

        Assert.Single(white.FindAll("[data-testid=to-move]"));
        Assert.Equal("true", white.Find("[data-testid=player-white]").GetAttribute("data-to-move"));
        Assert.Contains("player-bar-active", white.Find("[data-testid=player-white]").ClassList);
        Assert.Empty(black.FindAll("[data-testid=to-move]"));
        Assert.Equal("false", black.Find("[data-testid=player-black]").GetAttribute("data-to-move"));
    }

    /// <summary>
    /// The bar keeps one height through the game, so the board under it never moves: the badge, the thinking line, the
    /// captures and both usage lines are rendered in every state, hidden when they have nothing to say.
    /// </summary>
    [Fact]
    public void EveryRowIsRenderedInEveryStateSoTheBarKeepsItsHeight()
    {
        using var ctx = new BunitContext();

        var idle = ctx.Render<PlayerBar>(p => p.Add(x => x.Color, "Black").Add(x => x.Name, "Claude"));
        var busy = ctx.Render<PlayerBar>(p => p
            .Add(x => x.Color, "White").Add(x => x.Name, "GPT").Add(x => x.ToMove, true).Add(x => x.Attempt, 1)
            .Add(x => x.CapturedHere, "♟").Add(x => x.Totals, new SideTotals(2, 1_200, 300, 2.5)));

        foreach (var bar in new[] { idle, busy })
        {
            Assert.Single(bar.FindAll(".player-name .badge"));
            Assert.Single(bar.FindAll(".player-thinking"));
            Assert.Single(bar.FindAll(".player-captured"));
            Assert.Equal(2, bar.FindAll(".player-usage > span").Count);
        }

        Assert.Equal(3, idle.FindAll(".player-placeholder").Count);
        Assert.All(idle.FindAll(".player-placeholder"), placeholder => Assert.Equal("true", placeholder.GetAttribute("aria-hidden")));
        Assert.Empty(busy.FindAll(".player-placeholder"));
    }

    [Fact]
    public void ThinkingShowsTheSecondsAndTheAttemptOutOfThree()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlayerBar>(p => p
            .Add(x => x.Color, "White").Add(x => x.Name, "GPT").Add(x => x.Attempt, 2).Add(x => x.Seconds, 7));

        Assert.Equal("Thinking for 7 s · attempt 2 of 3", TextWords.Words(cut.Find("[data-testid=player-thinking]").TextContent));
    }

    [Fact]
    public void AnAttemptPastTheLimitShowsTheLimit()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlayerBar>(p => p.Add(x => x.Color, "Black").Add(x => x.Name, "Claude").Add(x => x.Attempt, 4));

        Assert.Contains("attempt 3 of 3", cut.Find("[data-testid=player-thinking]").TextContent);
    }

    [Fact]
    public void RunningTokensAndTheEstimatedCostAreShownForTheSide()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlayerBar>(p => p
            .Add(x => x.Color, "White").Add(x => x.Name, "GPT")
            .Add(x => x.Totals, new SideTotals(2, 1_200, 300, 2.5)));

        var usage = cut.Find("[data-testid=player-usage]").TextContent;
        Assert.Contains("1,200 in · 300 out", usage);
        Assert.Contains("≈ $0.03 est.", usage);
    }

    [Fact]
    public void ASideWithoutRequestsSaysSoAndTheLeadIsShownOnlyWhenPositive()
    {
        using var ctx = new BunitContext();

        var ahead = ctx.Render<PlayerBar>(p => p
            .Add(x => x.Color, "White").Add(x => x.Name, "Random").Add(x => x.Lead, 3).Add(x => x.CapturedHere, "♟♟♟"));
        var behind = ctx.Render<PlayerBar>(p => p.Add(x => x.Color, "Black").Add(x => x.Name, "Random").Add(x => x.Lead, 0));

        Assert.Equal("+3", ahead.Find("[data-testid=player-lead]").TextContent);
        Assert.Contains("♟♟♟", ahead.Find("[data-testid=player-captured]").TextContent);
        Assert.Empty(behind.FindAll("[data-testid=player-lead]"));
        Assert.Contains("No model requests yet", behind.Find("[data-testid=player-usage]").TextContent);
    }
}

public class EngineLogTests
{
    [Fact]
    public void EachAttemptIsAnEntryNewestFirstWithItsPromptReplyAndReasoning()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<EngineLog>(p => p.Add(x => x.Entries, [
            Entry(1, 1, legal: true, reasoning: "Open the centre."),
            Entry(2, 1, legal: false, kind: "illegal"),
            Entry(2, 2, legal: true),
        ]));

        var entries = cut.FindAll("[data-testid=engine-entry]");
        Assert.Equal(new[] { "2:2", "2:1", "1:1" }, entries.Select(e => $"{e.GetAttribute("data-ply")}:{e.GetAttribute("data-attempt")}"));
        Assert.Contains("Open the centre.", entries[2].QuerySelector("[data-testid=entry-reasoning]")!.TextContent);
        Assert.Contains("Play e4", entries[2].QuerySelector("[data-testid=entry-prompt]")!.TextContent);
        Assert.Equal("e4", entries[2].QuerySelector("[data-testid=entry-reply]")!.TextContent);
        Assert.Contains("attempt 1 of 3", entries[2].TextContent);
    }

    [Fact]
    public void AModelThatExposedNoReasoningSaysSo()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<EngineLog>(p => p.Add(x => x.Entries, [Entry(1, 1, legal: true)]));

        Assert.Equal("This model did not expose its reasoning.", cut.Find("[data-testid=entry-no-reasoning]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=entry-reasoning]"));
    }

    [Fact]
    public void EachRejectionKindRendersItsBadge()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<EngineLog>(p => p.Add(x => x.Entries, [
            Entry(1, 1, legal: true),
            Entry(1, 2, legal: false, kind: "unparseable"),
            Entry(1, 3, legal: false, kind: "ambiguous"),
            Entry(2, 1, legal: false, kind: "illegal"),
            Entry(2, 2, legal: false, kind: "timeout"),
            Entry(2, 3, legal: false, kind: "error"),
        ]));

        var badges = cut.FindAll("[data-testid=verdict]").Select(badge => (badge.TextContent, badge.ClassList.Contains("badge-success"),
            badge.ClassList.Contains("badge-warning"), badge.ClassList.Contains("badge-danger"))).ToArray();
        Assert.Equal(
            new[]
            {
                ("error", false, false, true),
                ("timeout", false, false, true),
                ("illegal", false, false, true),
                ("ambiguous", false, true, false),
                ("unparseable", false, true, false),
                ("legal", true, false, false),
            },
            badges);
    }

    [Theory]
    [InlineData("all", 6)]
    [InlineData("rejected", 3)]
    [InlineData("white", 3)]
    [InlineData("black", 3)]
    public void TheFiltersShowAllRejectedOrOneSide(string filter, int expected)
    {
        using var ctx = new BunitContext();
        var cut = ctx.Render<EngineLog>(p => p.Add(x => x.Entries, [
            Entry(1, 1, legal: true), Entry(1, 2, legal: false, kind: "illegal"), Entry(1, 3, legal: true),
            Entry(2, 1, legal: false, kind: "unparseable"), Entry(2, 2, legal: true), Entry(2, 3, legal: false, kind: "timeout"),
        ]));

        cut.Find($"[data-testid=log-filter-{filter}]").Click();

        Assert.Equal(expected, cut.FindAll("[data-testid=engine-entry]").Count);
        Assert.Equal("true", cut.Find($"[data-testid=log-filter-{filter}]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void TheEntriesOfTheSelectedMoveAreHighlighted()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<EngineLog>(p => p
            .Add(x => x.Entries, [Entry(1, 1, legal: true), Entry(2, 1, legal: true)])
            .Add(x => x.SelectedPly, 2));

        var highlighted = cut.FindAll(".engine-entry-selected");
        Assert.Equal("2", Assert.Single(highlighted).GetAttribute("data-ply"));
    }

    [Fact]
    public void AGameWithoutAttemptsSaysSoAndTheModelNamesAreShown()
    {
        using var ctx = new BunitContext();

        var empty = ctx.Render<EngineLog>(p => p.Add(x => x.Entries, []));
        var named = ctx.Render<EngineLog>(p => p
            .Add(x => x.Entries, [Entry(1, 1, legal: true)])
            .Add(x => x.ModelName, id => id == "gpt-x" ? "GPT X" : id));

        Assert.Contains("No attempts yet", empty.Find("[data-testid=log-empty]").TextContent);
        Assert.Equal("GPT X", named.Find(".engine-model").TextContent);
    }

    private static EngineEntry Entry(int ply, int attempt, bool legal, string? kind = null, string? reasoning = null) =>
        new(ply, attempt, "gpt-x", $"Play {(legal ? "e4" : "Ke9")}", legal ? "e4" : "Ke9", reasoning, legal ? "e4" : null,
            legal, kind, 900, 120, 30, 8, 0.25, DateTimeOffset.Parse("2026-10-24T10:00:00Z"));
}

public class GameUsageSummaryTests
{
    [Theory]
    [InlineData("free", "free (random player)")]
    [InlineData("reported", "reported by Copilot")]
    [InlineData("estimated", "estimated from token prices")]
    [InlineData("partial", "partly estimated (some replies unknown)")]
    [InlineData("unknown", "unknown")]
    public void EveryCostBasisIsNamed(string basis, string words)
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<GameUsageSummary>(p => p.Add(x => x.Usage, Usage(basis)));

        var row = cut.Find("[data-testid=usage-white]");
        Assert.Equal(basis, row.GetAttribute("data-basis"));
        Assert.Equal(words, row.QuerySelector(".badge")!.TextContent);
    }

    [Fact]
    public void TheSummaryStatesTheRateAndTheDollarsAreEstimates()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<GameUsageSummary>(p => p.Add(x => x.Usage, Usage("estimated")));

        var rate = cut.Find("[data-testid=usage-rate]").TextContent;
        Assert.Contains("Dollars are estimates", rate);
        Assert.Contains("$0.0100 per AI credit", rate);
    }

    [Fact]
    public void TheTotalRowSumsBothSidesAndAnUnknownCostIsADash()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<GameUsageSummary>(p => p.Add(x => x.Usage, Usage("unknown")));

        var total = cut.Find("[data-testid=usage-total]").TextContent;
        Assert.Contains("1,400", total);
        Assert.Equal("—", cut.Find("[data-testid=usage-black] [data-testid=usage-usd]").TextContent);
        Assert.Equal("$0.25", cut.Find("[data-testid=usage-total-usd]").TextContent);
    }

    [Fact]
    public void WithoutUsageTheMessageIsShown()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<GameUsageSummary>(p => p.Add(x => x.Message, "Could not load the usage of this game: 404"));

        Assert.Contains("Could not load the usage", cut.Find("[data-testid=usage-pending]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=usage-table]"));
    }

    private static GameUsageDto Usage(string basis)
    {
        var id = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var white = new SideUsageDto("White", "gpt", 2, 1_000, 200, 40, 0, 0.5, 0.005, basis);
        var black = new SideUsageDto("Black", "claude", 1, 400, 100, 0, 0, null, null, "unknown");
        return new GameUsageDto(id, white, black, 0.5, 0.25, 0.01);
    }
}
