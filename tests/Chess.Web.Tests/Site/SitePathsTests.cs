using Chess.Web.Api;
using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>The rules of the export that need no app: where pages live, how links are written, what the games list holds.</summary>
public class SitePathsTests
{
    [Theory]
    [InlineData("alpha", "alpha")]
    [InlineData("GPT 5.1/x", "gpt-5.1-x")]
    [InlineData("claude-sonnet_4", "claude-sonnet-4")]
    [InlineData("", "-")]
    public void A_model_id_becomes_a_folder_name(string model, string folder) =>
        Assert.Equal(folder, SitePaths.Slug(model));

    [Fact]
    public void A_pair_page_is_named_by_the_two_ids_in_ordinal_order_under_head_to_head() =>
        Assert.Equal("head-to-head/alpha--beta/index.html", SitePaths.Pair("alpha", "beta"));

    [Theory]
    [InlineData(1, "games/index.html")]
    [InlineData(2, "games/page-2/index.html")]
    [InlineData(17, "games/page-17/index.html")]
    public void The_first_games_page_is_the_index_and_the_others_are_numbered(int page, string path) =>
        Assert.Equal(path, SitePaths.GamesPage(page));

    [Fact]
    public void A_link_climbs_one_level_per_folder_between_the_page_and_the_root()
    {
        Assert.Equal("index.html", new SiteFrame(0, SiteDataSet.Now, 0, null, []).Link("index.html"));
        Assert.Equal("../games/index.html", new SiteFrame(1, SiteDataSet.Now, 0, null, []).Link("games/index.html"));
        Assert.Equal("../../games/x/index.html", new SiteFrame(2, SiteDataSet.Now, 0, null, []).Link("games/x/index.html"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/@arena/live", "https://www.youtube.com/@arena/live")]
    [InlineData("http://example.org/a?b=c", "http://example.org/a?b=c")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,hi", null)]
    [InlineData("/donate", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_absolute_http_links_are_rendered(string? url, string? expected) =>
        Assert.Equal(expected, SiteLinks.Safe(url));

    [Fact]
    public void Finished_games_only_newest_first_and_a_page_is_twenty_of_them()
    {
        var games = Enumerable.Range(0, 45)
            .Select(i => Game(i, i < 40 ? "finished" : "running"))
            .ToList();

        var finished = SiteGames.Finished(games);

        Assert.Equal(40, finished.Count);
        Assert.Equal(games[0].Id, finished[0].Id);
        Assert.Equal(20, SiteGames.Page(finished, 1).Count);
        Assert.Equal(20, SiteGames.Page(finished, 2).Count);
        Assert.Empty(SiteGames.Page(finished, 3));
    }

    [Fact]
    public void Two_pairs_that_would_share_a_page_are_refused()
    {
        // "A B" and "a-b" both become the folder "a-b"; the second pair would overwrite the first page.
        var matrix = new MatrixDto(["A B", "a-b", "x"],
        [
            new PairwiseCoefficient("A B", "x", 0.5, 0, 0.5, 0.4, 0.6, 0, 0, null),
            new PairwiseCoefficient("x", "A B", 0.5, 0, 0.5, 0.4, 0.6, 0, 0, null),
            new PairwiseCoefficient("a-b", "x", 0.5, 0, 0.5, 0.4, 0.6, 0, 0, null),
            new PairwiseCoefficient("x", "a-b", 0.5, 0, 0.5, 0.4, 0.6, 0, 0, null),
        ]);
        var board = new LeaderboardDto([], 0, 0, false);

        Assert.Throws<InvalidOperationException>(() => SitePair.Build(matrix, board, []));
    }

    private static GameSummaryDto Game(int index, string state) =>
        new(Guid.Parse($"00000000-0000-4000-8000-{index:000000000000}"), null, new PlayerSpec("llm", "alpha"),
            new PlayerSpec("llm", "beta"), true, state, state == "finished" ? "1-0" : "*", null, 10,
            SiteDataSet.Now.AddMinutes(-index), null);
}
