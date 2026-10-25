using Chess.Web.Site.Game;
using Xunit;

namespace Chess.Web.Tests.Site.Game;

/// <summary>The pure rules of the game pages: which games keep the engine log, and where each ply's links go.</summary>
public class SiteGameRulesTests
{
    [Theory]
    [InlineData(0, 300, true)]
    [InlineData(299, 300, true)]
    [InlineData(300, 300, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, -1, false)]
    public void Only_the_newest_games_keep_the_engine_log(int newestFirstIndex, int fullLogGames, bool keeps) =>
        Assert.Equal(keeps, SiteGameRules.KeepsEngineLog(newestFirstIndex, fullLogGames));

    [Fact]
    public void The_start_position_has_no_link_back_and_the_last_ply_has_no_link_forward()
    {
        var start = new SitePlies(0, 7);
        Assert.Null(start.First);
        Assert.Null(start.Previous);
        Assert.Equal(1, start.Next);
        Assert.Equal(7, start.Last);

        var end = new SitePlies(7, 7);
        Assert.Equal(0, end.First);
        Assert.Equal(6, end.Previous);
        Assert.Null(end.Next);
        Assert.Null(end.Last);
    }

    [Fact]
    public void A_game_without_moves_has_no_links_to_follow()
    {
        var only = new SitePlies(0, 0);

        Assert.Null(only.First);
        Assert.Null(only.Previous);
        Assert.Null(only.Next);
        Assert.Null(only.Last);
    }

    [Fact]
    public void Each_ply_has_one_anchor_and_the_link_to_it_points_at_that_anchor()
    {
        Assert.Equal("ply-0", SitePlies.Anchor(0));
        Assert.Equal("ply-12", SitePlies.Anchor(12));
        Assert.Equal("#ply-12", SitePlies.Href(12));
    }
}
