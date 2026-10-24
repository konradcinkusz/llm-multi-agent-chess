using System.Globalization;
using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class LineChartTests
{
    private static readonly LineSeries Alpha = new("alpha", 0,
        new ChartPoint[] { new(1, 1500), new(2, 1520), new(3, 1510) });

    private static readonly LineSeries Beta = new("beta", 3,
        new ChartPoint[] { new(1, 1480), new(3, 1490) });

    [Fact]
    public void EachSeriesIsOnePolylineInItsSlotColour()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, [Alpha, Beta]);

        var lines = cut.FindAll("polyline.chart-line");
        Assert.Equal(2, lines.Count);
        Assert.Contains("var(--chart-series-3)", lines[1].GetAttribute("style"));
    }

    [Fact]
    public void ALegendAppearsForTwoOrMoreSeries()
    {
        using var ctx = new BunitContext();

        var single = Render(ctx, [Alpha]);
        Assert.Empty(single.FindAll(".chart-legend"));

        var both = Render(ctx, [Alpha, Beta]);
        Assert.Single(both.FindAll(".chart-legend"));
        Assert.Equal(new[] { "alpha", "beta" }, both.FindAll(".chart-legend li").Select(li => li.TextContent));
    }

    [Fact]
    public void EndLabelsAreTextInkNeverTheSeriesColour()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, [Alpha, Beta]);

        var labels = cut.FindAll("text.chart-label");
        Assert.Equal(new[] { "alpha", "beta" }, labels.Select(t => t.TextContent));
        Assert.All(labels, t => Assert.Null(t.GetAttribute("style")));
    }

    [Fact]
    public void EveryPointHasAHoverTarget()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, [Alpha, Beta]);

        var titles = cut.FindAll("circle.chart-hit > title").Select(t => t.TextContent).ToList();
        Assert.Equal(5, titles.Count);
        Assert.Contains("alpha, Games played 1: 1500", titles);
    }

    [Fact]
    public void EndLabelsOfEqualValuesAreSeparated()
    {
        using var ctx = new BunitContext();
        var tied = new LineSeries("tied-a", 0, new ChartPoint[] { new(1, 1500), new(3, 1500) });
        var other = new LineSeries("tied-b", 1, new ChartPoint[] { new(1, 1400), new(3, 1500) });

        var cut = Render(ctx, [tied, other]);

        var ys = cut.FindAll("text.chart-label")
            .Select(t => double.Parse(t.GetAttribute("y")!, CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(ys[1] - ys[0] >= 14 - 0.001, $"labels {ys[0]} and {ys[1]} are closer than 14 px");
    }

    [Fact]
    public void TheTableViewListsEveryPointInLongFormat()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, [Alpha, Beta]);

        Assert.Equal(5, cut.FindAll("table tbody tr").Count);
        Assert.Equal(new[] { "Series", "Games played", "Rating" }, cut.FindAll("table thead th").Select(th => th.TextContent));
    }

    private static IRenderedComponent<LineChart> Render(BunitContext ctx, IReadOnlyList<LineSeries> series) =>
        ctx.Render<LineChart>(p => p
            .Add(b => b.Title, "Rating over time")
            .Add(b => b.XTitle, "Games played")
            .Add(b => b.YTitle, "Rating")
            .Add(b => b.Series, series));
}
