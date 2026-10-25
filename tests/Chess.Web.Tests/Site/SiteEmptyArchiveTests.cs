extern alias ChessWeb;

using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>A fresh arena has no games: the export still writes every page, and each one says what is missing.</summary>
public class SiteEmptyArchiveTests(EmptySiteFixture site) : IClassFixture<EmptySiteFixture>
{
    [Fact]
    public void The_empty_export_writes_only_the_pages_that_do_not_depend_on_a_game()
    {
        var pages = site.Pages.Keys.OrderBy(page => page, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["about/index.html", "games/index.html", "head-to-head/index.html", "index.html", "tournaments/index.html"],
            pages);
    }

    [Fact]
    public void Each_page_says_what_is_missing_instead_of_showing_an_empty_chart()
    {
        Assert.Contains("No rated games yet", site.Pages["index.html"], StringComparison.Ordinal);
        Assert.Contains("At least two rated models are needed to compare.", site.Pages["head-to-head/index.html"], StringComparison.Ordinal);
        Assert.Contains("No finished games yet.", site.Pages["games/index.html"], StringComparison.Ordinal);
        Assert.Contains("No tournaments yet.", site.Pages["tournaments/index.html"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_export_has_no_broken_links()
    {
        Assert.Empty(LinkCheck.Problems(site.Directory, site.Pages, new HashSet<string>(StringComparer.Ordinal)));
    }
}

/// <summary>An export of an arena with no games at all.</summary>
public sealed class EmptySiteFixture : ExportedSite
{
    public override Task InitializeAsync() => ExportWith(empty: true);
}
