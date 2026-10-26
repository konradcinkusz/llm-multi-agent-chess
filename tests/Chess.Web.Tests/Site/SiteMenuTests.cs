extern alias ChessWeb;

using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>The menu page of the public site (03 §15.6): the price matrix from the quotes, and no script.</summary>
public class SiteMenuTests(SiteExportFixture site) : IClassFixture<SiteExportFixture>
{
    private string Html => site.Pages[SitePaths.Menu];

    /// <summary>The part of the page that holds one style's matrix, from its heading to the next style or the end.</summary>
    private string Section(string style)
    {
        var start = Html.IndexOf($"data-style=\"{style}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the {style} matrix is on the page");
        var next = Html.IndexOf("data-style=\"", start + 1, StringComparison.Ordinal);
        return next < 0 ? Html[start..] : Html[start..next];
    }

    [Fact]
    public void The_menu_lists_each_entry_with_its_code_and_its_cost_per_side()
    {
        Assert.Contains("data-code=\"c1\"", Html);
        Assert.Contains("$0.40 a side", Html);
        Assert.Contains("data-code=\"d3\"", Html);
        Assert.Contains("$2.00 a side", Html);
    }

    [Fact]
    public void The_matrix_has_a_cell_per_matchup_in_classic_and_think()
    {
        foreach (var style in SitePriceMenu.Styles)
        {
            var section = Section(style);
            foreach (var white in new[] { "c1", "d3" })
            {
                foreach (var black in new[] { "c1", "d3" })
                {
                    Assert.Contains($"data-white=\"{white}\" data-black=\"{black}\"", section);
                }
            }
        }
    }

    [Fact]
    public void A_cheap_matchup_shows_its_price_and_a_matchup_above_the_tiers_shows_none()
    {
        // The test quote: $1 between the two cheap entries, $5 otherwise, and no price against a Claude side in think.
        Assert.Contains("data-white=\"c1\" data-black=\"c1\" data-price=\"$1.00\"", Section("classic"));
        Assert.Contains("data-white=\"c1\" data-black=\"d3\" data-price=\"$5.00\"", Section("classic"));
        Assert.Contains("data-white=\"c1\" data-black=\"d3\" data-price=\"none\"", Section("think"));
    }

    [Fact]
    public void The_menu_page_has_no_script_and_no_blazor_reference()
    {
        Assert.DoesNotContain("<script", Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor", Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_page_of_the_export_links_to_the_menu()
    {
        var pages = site.Pages.Values.Where(page => page.Contains("class=\"nav\"", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(pages);
        Assert.All(pages, page => Assert.Contains(">Menu</a>", page));
    }
}
