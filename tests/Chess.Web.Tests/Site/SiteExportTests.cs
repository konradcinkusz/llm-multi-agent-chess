extern alias ChessWeb;

using System.Text.Json;
using System.Text.RegularExpressions;
using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>The public site of 03 §11 exported from the default data set: pages, links, scripts, data and the Results order.</summary>
public class SiteExportTests(SiteExportFixture site) : IClassFixture<SiteExportFixture>
{
    private static readonly string[] ExpectedPages =
    [
        "index.html",
        "head-to-head/index.html",
        "head-to-head/alpha--beta/index.html",
        "head-to-head/alpha--gamma/index.html",
        "head-to-head/beta--gamma/index.html",
        "games/index.html",
        "games/page-2/index.html",
        "tournaments/index.html",
        $"tournaments/{SiteDataSet.Tournament:D}/index.html",
        "about/index.html",
    ];

    private static readonly string[] ExpectedData =
    [
        "data/snapshot.json", "data/leaderboard.json", "data/matrix.json", "data/history.json", "data/breakdown.json",
        "data/tournaments.json", "data/games.json", "data/usage.json",
    ];

    private static readonly string[] ExpectedAssets = ["assets/app.css", "assets/Chess.Web.styles.css", "assets/favicon.png", ".nojekyll"];

    private static readonly string[] ResultsSections =
    [
        "big-picture", "rating-history", "head-to-head", "breakdown", "move-quality", "strength-cost", "table",
    ];

    private ExportedSite Site => site;

    [Fact]
    public void Every_page_of_the_export_exists()
    {
        var missing = ExpectedPages.Concat(ExpectedData).Concat(ExpectedAssets)
            .Where(path => !File.Exists(Path.Combine(Site.Directory, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_export_has_no_page_it_does_not_list()
    {
        var pages = Directory.EnumerateFiles(Site.Directory, "*.html", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Site.Directory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedPages.OrderBy(p => p, StringComparer.Ordinal), pages);
    }

    [Fact]
    public void The_result_counts_the_pages_games_and_bytes_it_wrote()
    {
        var files = Directory.EnumerateFiles(Site.Directory, "*", SearchOption.AllDirectories).ToArray();

        Assert.Equal(ExpectedPages.Length, Site.Result.Pages);
        Assert.Equal(SiteDataSet.FinishedGames, Site.Result.Games);
        Assert.Equal(files.Sum(file => new FileInfo(file).Length), Site.Result.Bytes);
        Assert.Equal(SiteDataSet.Now, Site.Result.TakenAt);
    }

    [Fact]
    public void Every_page_is_a_whole_html_document()
    {
        foreach (var (path, html) in Site.Pages)
        {
            Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.Contains("</html>", html, StringComparison.Ordinal);
            Assert.Matches(@"<title[\s>]", html);
        }
    }

    [Fact]
    public void No_page_has_a_script_or_a_blazor_reference()
    {
        foreach (var (path, html) in Site.Pages)
        {
            Assert.False(html.Contains("<script", StringComparison.OrdinalIgnoreCase), $"{path} has a script element.");
            Assert.False(html.Contains("blazor", StringComparison.OrdinalIgnoreCase), $"{path} refers to Blazor.");
        }
    }

    [Fact]
    public void The_index_starts_with_the_big_picture()
    {
        var html = Site.Pages["index.html"];
        var firstSection = Regex.Match(html[html.IndexOf("<main", StringComparison.Ordinal)..], "<section[^>]*data-section=\"([^\"]+)\"");

        Assert.True(firstSection.Success);
        Assert.Equal("big-picture", firstSection.Groups[1].Value);
    }

    [Fact]
    public void The_results_sections_follow_the_order_of_the_design()
    {
        var sections = Regex.Matches(Site.Pages["index.html"], "data-section=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(ResultsSections.Contains)
            .ToArray();

        Assert.Equal(ResultsSections, sections);
    }

    [Fact]
    public void The_charts_are_inline_svg_with_their_table_view()
    {
        var html = Site.Pages["index.html"];

        Assert.True(Regex.Matches(html, "<svg class=\"chart-svg\"").Count >= 5);
        Assert.Contains("<details class=\"chart-table\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>alpha: 70.0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_is_relative_and_resolves_to_a_file_of_the_export()
    {
        // The game pages are linked before S2 renders them; their links are the only ones allowed to miss a file.
        var problems = LinkCheck.Problems(Site.Directory, Site.Pages, new HashSet<string>(StringComparer.Ordinal));

        Assert.True(LinkCheck.LinksOf(Site.Pages).Count() > 100, "The link check found too few links to mean anything.");
        Assert.Empty(problems);
    }

    [Fact]
    public void The_games_list_links_each_finished_game_once_and_only_finished_games()
    {
        var page1 = Regex.Matches(Site.Pages["games/index.html"], "games/([0-9a-f-]{36})/index\\.html").Select(m => m.Groups[1].Value).ToArray();
        var page2 = Regex.Matches(Site.Pages["games/page-2/index.html"], "games/([0-9a-f-]{36})/index\\.html").Select(m => m.Groups[1].Value).ToArray();
        var listed = page1.Concat(page2).ToArray();

        Assert.Equal(20, page1.Length);
        Assert.Equal(SiteDataSet.FinishedGames - 20, page2.Length);
        Assert.Equal(SiteDataSet.FinishedGames, listed.Distinct().Count());
        Assert.DoesNotContain(SiteDataSet.OpenGame.ToString("D"), listed);
        Assert.DoesNotContain(SiteDataSet.QueuedGame.ToString("D"), listed);
    }

    [Fact]
    public void The_games_list_shows_the_estimated_cost_of_each_game()
    {
        var games = Site.Pages["games/index.html"];

        Assert.Matches(@"\$\d\.\d\d", games);
        Assert.Contains("2.7500 cr", games, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_head_to_head_pair_states_its_reading_and_its_direct_record()
    {
        var pair = Site.Pages["head-to-head/alpha--beta/index.html"];

        Assert.Contains("Direct record", pair, StringComparison.Ordinal);
        Assert.Contains("for alpha", pair, StringComparison.Ordinal);
        Assert.Contains("← All pairs", pair, StringComparison.Ordinal);
        Assert.Contains("RQC for every pairing", Site.Pages["head-to-head/index.html"], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_page_shows_the_snapshot_time_and_the_rated_game_count()
    {
        foreach (var (path, html) in Site.Pages)
        {
            Assert.True(html.Contains("Snapshot taken 2026-10-25 12:00 UTC", StringComparison.Ordinal), $"{path} has no snapshot time.");
            Assert.True(html.Contains("24 rated games", StringComparison.Ordinal), $"{path} has no rated game count.");
        }
    }

    [Fact]
    public void The_tournament_page_shows_its_own_leaderboard_and_games()
    {
        var html = Site.Pages[$"tournaments/{SiteDataSet.Tournament:D}/index.html"];

        Assert.Contains("Spring", html, StringComparison.Ordinal);
        Assert.Contains("<td>alpha</td>", html, StringComparison.Ordinal);
        Assert.Contains($"../../games/{SiteDataSet.GameId(0):D}/index.html", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_data_file_is_valid_json_with_the_snapshot_it_describes()
    {
        foreach (var file in ExpectedData)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Site.Directory, file.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Contains(document.RootElement.ValueKind, new[] { JsonValueKind.Object, JsonValueKind.Array });
        }

        var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(Site.Directory, "data", "snapshot.json"))).RootElement;
        Assert.Equal(24, snapshot.GetProperty("ratedGames").GetInt32());
        Assert.Equal(SiteDataSet.FinishedGames, snapshot.GetProperty("games").GetInt32());

        var games = JsonDocument.Parse(File.ReadAllText(Path.Combine(Site.Directory, "data", "games.json"))).RootElement;
        Assert.Equal(SiteDataSet.FinishedGames, games.GetArrayLength());
    }

    [Fact]
    public void The_pages_are_not_rendered_with_a_watch_live_or_support_link_when_none_is_configured()
    {
        foreach (var (path, html) in Site.Pages)
        {
            Assert.False(html.Contains("Watch live", StringComparison.Ordinal), $"{path} shows Watch live.");
            Assert.False(html.Contains("site-support", StringComparison.Ordinal), $"{path} shows support links.");
        }
    }
}

/// <summary>The default export, shared by the tests of one class.</summary>
public sealed class SiteExportFixture : ExportedSite;
