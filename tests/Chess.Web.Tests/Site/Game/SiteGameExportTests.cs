using System.Text.Json;
using System.Text.RegularExpressions;
using Chess.Web.Api;
using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Site.Game;

/// <summary>
/// The per-game pages and PGN files of the export (03 §11), checked on the default data set: one folder per finished game,
/// the PGN the API gives, an anchor per ply, the engine log only for the newest games, and links that all resolve.
/// </summary>
public class SiteGameExportTests(SiteGameFixture site) : IClassFixture<SiteGameFixture>
{
    private static readonly Regex PlyAnchor = new("id=\"ply-\\d+\"", RegexOptions.Compiled);

    /// <summary>A game's page: <c>games/&lt;id&gt;/index.html</c>, not a page of the games list (<c>games/page-2/index.html</c>).</summary>
    private static readonly Regex GamePagePath = new("^games/[0-9a-f-]{36}/index\\.html$", RegexOptions.Compiled);

    private List<GameSummaryDto> Games => ReadGames(site.Directory);

    private IReadOnlyDictionary<string, string> GamePages => site.Pages
        .Where(page => GamePagePath.IsMatch(page.Key))
        .ToDictionary(page => page.Key, page => page.Value, StringComparer.Ordinal);

    [Fact]
    public void Every_finished_game_has_a_folder_with_its_page_and_its_pgn_and_no_other_game_does()
    {
        var folders = Directory.GetDirectories(Path.Combine(site.Directory, "games"))
            .Select(Path.GetFileName)
            .Where(name => Guid.TryParse(name, out _))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expected = Games.Select(game => game.Id.ToString("D")).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(SiteDataSet.FinishedGames, expected.Length);
        Assert.Equal(expected, folders);
        Assert.DoesNotContain(SiteDataSet.OpenGame.ToString("D"), folders);
        foreach (var game in Games)
        {
            Assert.True(File.Exists(Path.Combine(site.Directory, "games", game.Id.ToString("D"), "game.pgn")), $"{game.Id} has no PGN.");
        }
    }

    [Fact]
    public void The_pgn_file_is_the_pgn_the_api_gives_for_the_game()
    {
        foreach (var game in Games)
        {
            var file = File.ReadAllText(Path.Combine(site.Directory, "games", game.Id.ToString("D"), "game.pgn"));

            Assert.Equal(SiteGameDataSet.PgnText(game), file);
            Assert.Contains($"[Result \"{game.Result}\"]", file, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_ply_of_every_game_has_its_anchor_and_the_start_board_has_a_link_back_to_the_first_move()
    {
        foreach (var game in Games)
        {
            var html = site.Pages[SitePaths.Game(game.Id)];

            Assert.Equal(game.PlyCount + 1, PlyAnchor.Matches(html).Count);
            Assert.Contains($"href=\"#ply-{game.PlyCount}\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_engine_log_is_kept_for_the_newest_games_only()
    {
        var keep = 2;
        var (directory, _) = ExportWith(("Arena:Site:FullLogGames", keep.ToString()));
        try
        {
            var games = ReadGames(directory);
            for (var index = 0; index < games.Count; index++)
            {
                var html = File.ReadAllText(Path.Combine(directory, SitePaths.Game(games[index].Id)));

                if (index < keep)
                {
                    Assert.Contains("data-section=\"log-entries\"", html, StringComparison.Ordinal);
                    Assert.DoesNotContain("data-section=\"log-omitted\"", html, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain("data-section=\"log-entries\"", html, StringComparison.Ordinal);
                    Assert.Contains("data-section=\"log-omitted\"", html, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_log_shows_each_prompt_and_reply_and_the_reasoning_or_that_it_is_missing()
    {
        var html = GamePages.Values.First(page => page.Contains("data-section=\"log-entries\"", StringComparison.Ordinal));

        Assert.Contains("Prompt for ply 1", html, StringComparison.Ordinal);
        Assert.Contains("Developing the knight.", html, StringComparison.Ordinal);
        Assert.Contains("did not expose its reasoning", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_game_page_is_a_whole_document_with_no_script_and_no_blazor_reference()
    {
        Assert.Equal(SiteDataSet.FinishedGames, GamePages.Count);
        foreach (var (path, html) in GamePages)
        {
            Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.False(html.Contains("<script", StringComparison.OrdinalIgnoreCase), $"{path} has a script element.");
            Assert.False(html.Contains("blazor", StringComparison.OrdinalIgnoreCase), $"{path} refers to Blazor.");
        }
    }

    [Fact]
    public void Every_link_of_every_game_page_is_relative_and_resolves()
    {
        var problems = LinkCheck.Problems(site.Directory, GamePages, new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(problems);
        Assert.True(LinkCheck.LinksOf(GamePages).Count() > 500, "The link check found too few links to mean anything.");
    }

    [Fact]
    public void Every_game_page_shows_its_usage_the_snapshot_time_and_the_tournament_it_belongs_to()
    {
        foreach (var game in Games)
        {
            var html = site.Pages[SitePaths.Game(game.Id)];

            Assert.Contains("data-testid=\"usage-total\"", html, StringComparison.Ordinal);
            Assert.Contains("Snapshot taken 2026-10-25 12:00 UTC", html, StringComparison.Ordinal);
            if (game.TournamentId is { } tournament)
            {
                Assert.Contains($"../../tournaments/{tournament:D}/index.html", html, StringComparison.Ordinal);
            }
        }
    }

    private (string Directory, SiteExportResult Result) ExportWith(params (string Key, string Value)[] settings)
    {
        using var app = new SiteExportApp(false, settings);
        return app.ExportAsync().GetAwaiter().GetResult();
    }

    private static List<GameSummaryDto> ReadGames(string directory) =>
        JsonSerializer.Deserialize<List<GameSummaryDto>>(
            File.ReadAllText(Path.Combine(directory, "data", "games.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}

/// <summary>The default export, shared by the game page tests of one class.</summary>
public sealed class SiteGameFixture : ExportedSite;
