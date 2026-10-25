using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Shared.Game;
using Chess.Web.Components.Site.Game;
using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Site.Game;

/// <summary>
/// The game page of a short game (Scholar's mate, seven plies): one board per ply behind its anchor, the links that step
/// between them, the move list, the usage, the engine log and the PGN link. Rendered without a browser.
/// </summary>
public class SiteGamePageTests
{
    private static readonly string[] ScholarsMate = ["e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qxf7#"];

    private static readonly GameSummaryDto Summary = new(Guid.Parse("00000000-0000-4000-8000-00000000c0de"), null,
        new PlayerSpec("llm", "alpha"), new PlayerSpec("llm", "beta"), true, "finished", "1-0", "checkmate", ScholarsMate.Length,
        SiteDataSet.Now.AddMinutes(-5), SiteDataSet.Now.AddMinutes(-4));

    private static readonly GameUsageDto Usage = new(Summary.Id,
        new SideUsageDto("white", "alpha", 4, 2_000, 80, 0, 0, 1.25, 0.0125, "estimated"),
        new SideUsageDto("black", "beta", 3, 1_500, 60, 0, 0, 1.5, 0.015, "estimated"),
        2.75, 0.0275, 0.01);

    private static readonly SiteFrame Frame = new(2, SiteDataSet.Now, 24, null, []);

    [Fact]
    public void Every_ply_has_a_board_behind_its_anchor_and_the_start_board_is_the_one_shown_without_one()
    {
        using var context = new BunitContext();
        var page = Render(context);

        var boards = page.FindAll("figure.ply-board");
        Assert.Equal(ScholarsMate.Length + 1, boards.Count);
        for (var ply = 0; ply <= ScholarsMate.Length; ply++)
        {
            Assert.NotNull(page.Find($"figure#ply-{ply}"));
        }

        Assert.True(page.Find("figure#ply-0").ClassList.Contains("ply-start"));
    }

    [Fact]
    public void A_board_links_to_the_first_previous_next_and_last_ply()
    {
        using var context = new BunitContext();
        var page = Render(context);

        var links = page.Find("figure#ply-3").QuerySelectorAll("a")
            .Select(link => (link.TextContent, link.GetAttribute("href")))
            .ToArray();

        Assert.Equal([("First", "#ply-0"), ("Previous", "#ply-2"), ("Next", "#ply-4"), ("Last", "#ply-7")], links);
    }

    [Fact]
    public void The_links_past_the_ends_of_the_game_are_shown_disabled()
    {
        using var context = new BunitContext();
        var page = Render(context);

        var start = page.Find("figure#ply-0");
        Assert.Equal(["First", "Previous"], start.QuerySelectorAll("[aria-disabled='true']").Select(e => e.TextContent));
        Assert.Equal("#ply-1", start.QuerySelector("a[href='#ply-1']")!.GetAttribute("href"));

        var last = page.Find("figure#ply-7");
        Assert.Equal(["Next", "Last"], last.QuerySelectorAll("[aria-disabled='true']").Select(e => e.TextContent));
        Assert.Null(last.QuerySelector("a[href='#ply-8']"));
    }

    [Fact]
    public void The_move_list_links_each_move_to_the_board_after_it()
    {
        using var context = new BunitContext();
        var page = Render(context);

        var moves = page.FindAll("a.move-cell")
            .Select(cell => $"{cell.GetAttribute("data-ply")} {cell.GetAttribute("href")}")
            .ToArray();

        Assert.Equal(Enumerable.Range(1, ScholarsMate.Length).Select(ply => $"{ply} #ply-{ply}"), moves);
    }

    [Fact]
    public void Each_board_places_its_pieces_from_the_symbols_the_page_defines_once()
    {
        using var context = new BunitContext();
        var page = Render(context);

        // The start position has 32 pieces; the 12 symbols are defined once for the whole page, not once per board.
        Assert.Equal(32, page.Find("figure#ply-0").QuerySelectorAll("use[data-piece]").Count());
        Assert.Equal(12, page.FindAll("symbol").Count);
    }

    [Fact]
    public void The_board_of_a_move_highlights_the_squares_it_moved_between()
    {
        using var context = new BunitContext();
        var page = Render(context);

        var highlighted = page.Find("figure#ply-7").QuerySelectorAll("[data-highlight]")
            .Select(square => square.GetAttribute("data-highlight") ?? "")
            .Order()
            .ToArray();

        Assert.Equal(["f7", "h5"], highlighted);
        Assert.Empty(page.Find("figure#ply-0").QuerySelectorAll("[data-highlight]"));
    }

    [Fact]
    public void The_caption_names_the_move_and_the_ply_count()
    {
        using var context = new BunitContext();
        var page = Render(context);

        Assert.Equal("Ply 7 of 7 · White Qxf7#", page.Find("figure#ply-7 [data-section='caption']").TextContent);
        Assert.Equal("Start position", page.Find("figure#ply-0 [data-section='caption']").TextContent);
    }

    [Fact]
    public void The_header_states_the_result_and_the_length()
    {
        using var context = new BunitContext();
        var summary = Render(context).Find("[data-section='summary']").TextContent;

        Assert.Contains("result 1-0", summary, StringComparison.Ordinal);
        Assert.Contains("7 plies", summary, StringComparison.Ordinal);
        Assert.Contains("checkmate", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pgn_is_a_download_beside_the_page()
    {
        using var context = new BunitContext();
        var link = Render(context).Find("a[data-section='pgn']");

        Assert.Equal("game.pgn", link.GetAttribute("href"));
        Assert.True(link.HasAttribute("download"));
    }

    [Fact]
    public void The_usage_of_each_side_and_the_total_are_shown()
    {
        using var context = new BunitContext();
        var page = Render(context, usage: Usage);

        Assert.NotNull(page.Find("[data-testid='usage-white']"));
        Assert.NotNull(page.Find("[data-testid='usage-black']"));
        Assert.Contains("2.7500 cr", page.Find("[data-testid='usage-total']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_usage_says_it_is_not_loaded_when_it_is_missing()
    {
        using var context = new BunitContext();
        var page = Render(context, usage: null);

        Assert.Empty(page.FindAll("[data-testid='usage-table']"));
        Assert.NotNull(page.Find("[data-testid='usage-pending']"));
    }

    [Fact]
    public void The_engine_log_lists_the_attempts_newest_first_with_their_verdicts()
    {
        using var context = new BunitContext();
        var page = Render(context);

        // Ply 1 has two attempts: the rejected first one sorts after the legal second one, as the newest-first order says.
        var entries = page.FindAll("li.engine-entry");
        Assert.Equal(ScholarsMate.Length + 1, entries.Count);
        Assert.Equal("7", entries[0].GetAttribute("data-ply"));
        Assert.Equal(("1", "true"), (entries[^2].GetAttribute("data-ply"), entries[^2].GetAttribute("data-legal")));
        Assert.Equal(("1", "false"), (entries[^1].GetAttribute("data-ply"), entries[^1].GetAttribute("data-legal")));
        Assert.Contains("illegal", entries[^1].QuerySelector("[data-testid='verdict']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_game_that_does_not_keep_its_log_says_so_instead_of_showing_one()
    {
        using var context = new BunitContext();
        var page = Render(context, keepsLog: false);

        Assert.NotNull(page.Find("[data-section='log-omitted']"));
        Assert.Empty(page.FindAll("[data-section='log-entries']"));
    }

    [Fact]
    public void A_reasoning_is_shown_or_marked_as_not_exposed()
    {
        using var context = new BunitContext();
        var page = Render(context);

        Assert.Contains("Developing the knight.", page.Find("li[data-color='White'][data-ply='3'] [data-testid='entry-reasoning']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(page.Find("li[data-color='Black'][data-ply='2'] [data-testid='entry-no-reasoning']"));
    }

    [Fact]
    public void No_script_is_rendered()
    {
        using var context = new BunitContext();
        var page = Render(context);

        Assert.Empty(page.FindAll("script"));
        Assert.DoesNotContain("blazor", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private static IRenderedComponent<SiteGamePage> Render(BunitContext context, bool keepsLog = true, GameUsageDto? usage = null)
    {
        var game = SiteGameDataSet.Play(Summary, ScholarsMate);
        return context.Render<SiteGamePage>(parameters => parameters
            .Add(page => page.Frame, Frame)
            .Add(page => page.Game, game)
            .Add(page => page.Usage, usage)
            .Add(page => page.Attempts, Attempts(game))
            .Add(page => page.KeepsLog, keepsLog));
    }

    /// <summary>The engine log of the game: a rejected first reply at ply 1, then one legal reply per ply.</summary>
    private static IReadOnlyList<EngineEntry> Attempts(GameDetailDto game)
    {
        var entries = new List<EngineEntry>
        {
            new(1, 1, "alpha", "Prompt for ply 1", "Qxe9", null, null, false, "illegal", 900, 500, 20, null, 0.5, null),
        };
        entries.AddRange(game.Moves.Select(move => new EngineEntry(move.Ply, move.Ply == 1 ? 2 : 1,
            move.Color == "white" ? "alpha" : "beta", $"Prompt for ply {move.Ply}", move.San,
            move.Color == "white" ? "Developing the knight." : null, move.San, true, null, 1200, 500, 20, null, 1.25, null)));
        return entries;
    }
}
