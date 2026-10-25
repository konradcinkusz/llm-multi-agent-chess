using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

/// <summary>Chart polish (07 §6): degenerate axes, the bar and cell caps, and the stacked legend in stacking order.</summary>
public class ChartPolishTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void AScatterWhoseXValuesAreAllZeroShowsOneXTick()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ScatterPlot>(p => p
            .Add(s => s.Title, "Rating against latency")
            .Add(s => s.XTitle, "Latency")
            .Add(s => s.YTitle, "Rating")
            .Add(s => s.Points, new ScatterPoint[] { new("alpha", 0, 1500), new("beta", 0, 1620) }));

        var xTicks = cut.FindAll("text.chart-tick[text-anchor='middle']");
        Assert.Single(xTicks);
        Assert.Equal("0", xTicks[0].TextContent);

        // The points still plot: they sit inside the plot area, not on the y axis.
        foreach (var ring in cut.FindAll("circle.chart-ring"))
            Assert.True(double.Parse(ring.GetAttribute("cx")!, Invariant) > 56);
    }

    [Fact]
    public void ALineChartWithOneGameShowsOneXTickAndItsPoint()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<LineChart>(p => p
            .Add(l => l.Title, "Rating by rated games played")
            .Add(l => l.XTitle, "Games played")
            .Add(l => l.YTitle, "Rating")
            .Add(l => l.Series, new LineSeries[] { new("alpha", 0, [new ChartPoint(1, 1500)]) }));

        Assert.Single(cut.FindAll("text.chart-tick[text-anchor='middle']"));
        Assert.Single(cut.FindAll("circle.chart-ring"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NoBarIsThickerThanFortyEightPixels(int count)
    {
        using var ctx = new BunitContext();
        var items = Enumerable.Range(1, count).Select(i => new CategoryValue($"c{i}", 40 + i)).ToList();

        var cut = ctx.Render<BarChart>(p => p
            .Add(b => b.Title, "First-attempt legality")
            .Add(b => b.Items, items)
            .Add(b => b.Max, 100.0));

        var bars = cut.FindAll("g.chart-bar > path");
        Assert.Equal(count, bars.Count);
        foreach (var bar in bars)
        {
            var d = bar.GetAttribute("d")!;
            var left = double.Parse(Regex.Match(d, @"^M([\d.]+),").Groups[1].Value, Invariant);
            var right = double.Parse(Regex.Match(d, @"Q([\d.]+),[\d.]+ [\d.]+,[\d.]+V[\d.]+Z$").Groups[1].Value, Invariant);
            // 48 px on screen at the 1.5x cap (07 §6), so 32 px in the viewBox.
            Assert.True((right - left) * 1.5 <= 48, $"bar is {right - left} px wide in the viewBox");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public void HeatmapCellsNeverExceedSeventyTwoPixels(int models)
    {
        using var ctx = new BunitContext();
        var labels = Enumerable.Range(1, models).Select(i => $"model-{i:00}").ToList();
        var cells = labels.SelectMany(row => labels.Select(column => new HeatCell(row, column, 0.6))).ToList();

        var cut = ctx.Render<Heatmap>(p => p
            .Add(h => h.Title, "RQC for every pairing")
            .Add(h => h.Labels, labels)
            .Add(h => h.Cells, cells));

        Assert.Equal(models * models, cut.FindAll("g.chart-cell").Count);
        foreach (var rect in cut.FindAll("g.chart-cell > rect"))
            Assert.True(double.Parse(rect.GetAttribute("width")!, Invariant) <= 72);

        // The width attribute is the natural width; CSS scales the SVG to its card within the 1.5x cap.
        var svg = cut.Find("svg");
        var viewBoxWidth = double.Parse(svg.GetAttribute("viewBox")!.Split(' ')[2], Invariant);
        Assert.Equal(viewBoxWidth, double.Parse(svg.GetAttribute("width")!, Invariant));
    }

    [Fact]
    public void TheStackedLegendFollowsTheStackingOrderAndOutcomesKeepTheirColours()
    {
        using var ctx = new BunitContext();
        var rows = new[]
        {
            new StackRow("A",
            [
                new StackSegment("Wins", 2, 1),
                new StackSegment("Draws", 0, 1),
                new StackSegment("Losses", 7, 1),
                new StackSegment("Forfeit losses", 4, 1),
            ]),
        };

        var cut = ctx.Render<StackedBar100>(p => p
            .Add(b => b.Title, "Outcomes per model")
            .Add(b => b.Rows, rows));

        var legend = cut.FindAll(".chart-legend li").Select(li => li.TextContent).ToList();
        Assert.Equal(new[] { "Wins", "Draws", "Losses", "Forfeit losses" }, legend);

        var segmentNames = cut.FindAll("svg > path > title")
            .Select(t => Regex.Match(t.TextContent, @"· (.+): ").Groups[1].Value)
            .ToList();
        Assert.Equal(legend, segmentNames);

        var fills = cut.FindAll("svg > path").Select(p => p.GetAttribute("style")!).ToList();
        Assert.Contains("var(--chart-series-2)", fills[0]);
        Assert.Contains("var(--chart-neutral)", fills[1]);
        Assert.Contains("var(--chart-series-7)", fills[2]);
        Assert.Contains("url(#", fills[3]);
    }
}
