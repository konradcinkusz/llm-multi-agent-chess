using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class BarChartTests
{
    private static readonly CategoryValue[] Categories =
    [
        new("e4", 0.9),
        new("d4", 0.5),
    ];

    [Fact]
    public void OneBarPerCategoryWithATooltipAndAValueLabel()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<BarChart>(p => p
            .Add(b => b.Title, "First-attempt legal")
            .Add(b => b.Items, Categories));

        Assert.Equal(2, cut.FindAll("g.chart-bar").Count);
        Assert.Equal(2, cut.FindAll("g.chart-bar > path > title").Count);
        Assert.Equal("e4: 0.9", cut.Find("g[data-bar='e4'] > path > title").TextContent);
        Assert.Equal(new[] { "0.9", "0.5" }, cut.FindAll("text.chart-value").Select(t => t.TextContent));
    }

    [Fact]
    public void ASingleSeriesTakesTheFirstCategoricalSlot()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<BarChart>(p => p
            .Add(b => b.Title, "First-attempt legal")
            .Add(b => b.Items, Categories));

        Assert.Contains("var(--chart-series-0)", cut.Find("g[data-bar='e4'] > path").GetAttribute("style"));
    }

    [Fact]
    public void ABarChartHasNoLegend()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<BarChart>(p => p
            .Add(b => b.Title, "First-attempt legal")
            .Add(b => b.Items, Categories));

        Assert.Empty(cut.FindAll(".chart-legend"));
    }
}
