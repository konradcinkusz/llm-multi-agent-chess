using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Chess.Web.Components.Charts;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Chess.Web.Tests.Charts;

/// <summary>
/// U6 (07 §6): on a wide screen every chart fills its card, drawn no smaller than its natural (viewBox) width and no
/// larger than 1.5 times it. The heatmap's top band holds its rotated labels and no more.
/// </summary>
public class ChartFillTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void TheBarChartSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<BarChart>(p => p
            .Add(b => b.Title, "First-attempt legality")
            .Add(b => b.Items, new CategoryValue[] { new("alpha", 40), new("beta", 60) }));

        AssertFillsItsCard(cut);
    }

    [Fact]
    public void TheLineChartSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<LineChart>(p => p
            .Add(l => l.Title, "Rating over time")
            .Add(l => l.XTitle, "Games played")
            .Add(l => l.YTitle, "Rating")
            .Add(l => l.Series, new LineSeries[] { new("alpha", 0, [new ChartPoint(1, 1500), new ChartPoint(2, 1520)]) }));

        AssertFillsItsCard(cut);
    }

    [Fact]
    public void TheRankedBarChartSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<RankedBarChart>(p => p
            .Add(b => b.Title, "Arena score")
            .Add(b => b.Items, new RankedBar[] { new("alpha", 0.7), new("beta", 0.4) }));

        AssertFillsItsCard(cut);
    }

    [Fact]
    public void TheScatterPlotSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ScatterPlot>(p => p
            .Add(s => s.Title, "Strength vs cost")
            .Add(s => s.XTitle, "Average latency (s)")
            .Add(s => s.YTitle, "Rating")
            .Add(s => s.Points, new ScatterPoint[] { new("alpha", 0.5, 1500), new("beta", 1.5, 1620) }));

        AssertFillsItsCard(cut);
    }

    [Fact]
    public void TheStackedBarSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<StackedBar100>(p => p
            .Add(b => b.Title, "Results breakdown")
            .Add(b => b.Rows, new StackRow[] { new("alpha", [new StackSegment("Wins", 2, 3), new StackSegment("Losses", 7, 1)]) }));

        AssertFillsItsCard(cut);
    }

    [Fact]
    public void TheHeatmapSvgFillsItsCardWithinItsNaturalWidthAndOneAndAHalfTimesIt()
    {
        using var ctx = new BunitContext();
        var labels = new[] { "alpha", "beta", "gamma" };

        var cut = RenderHeatmap(ctx, labels);

        AssertFillsItsCard(cut);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    public void TheHeatmapTopBandHoldsItsRotatedLabelsAndNoMore(int models)
    {
        using var ctx = new BunitContext();
        var labels = Enumerable.Range(1, models).Select(i => $"gpt-like-model-{i:00}").ToList();

        var cut = RenderHeatmap(ctx, labels);

        // Each rotated column label is drawn 8 px above the top band: translate(x y) rotate(-45).
        var transform = cut.FindAll("text.chart-label[transform]")[0].GetAttribute("transform")!;
        var topBand = double.Parse(Regex.Match(transform, @"translate\([\d.]+ ([\d.]+)\)").Groups[1].Value, Invariant) + 8;

        // 12 px chart text, about 7.2 px a character. A 45° label rises by 0.71 of its length.
        var longest = labels.Max(l => l.Length) * 7.2;
        Assert.True(topBand <= longest + 8, $"top band {topBand} px is more than the label length {longest} px + 8");
        Assert.True(topBand >= longest * Math.Sqrt(0.5), $"top band {topBand} px clips a label that rises {longest * Math.Sqrt(0.5)} px");
    }

    [Fact]
    public void AHeatmapCellAtTheOneAndAHalfTimesCapIsNoWiderThanSeventyTwoPixels()
    {
        using var ctx = new BunitContext();
        var labels = new[] { "alpha", "beta" };

        var cut = RenderHeatmap(ctx, labels);

        var xs = cut.FindAll("g.chart-cell[data-row='alpha'] > rect")
            .Select(r => double.Parse(r.GetAttribute("x")!, Invariant))
            .OrderBy(x => x)
            .ToList();
        var pitch = xs[1] - xs[0];
        Assert.True(pitch * 1.5 <= 72, $"a cell is {pitch * 1.5} px on screen at the 1.5x cap");
    }

    private static IRenderedComponent<Heatmap> RenderHeatmap(BunitContext ctx, IReadOnlyList<string> labels)
    {
        var cells = labels.SelectMany(row => labels.Select(column => new HeatCell(row, column, 0.6))).ToList();
        return ctx.Render<Heatmap>(p => p
            .Add(h => h.Title, "RQC for every pairing")
            .Add(h => h.Labels, labels)
            .Add(h => h.Cells, cells));
    }

    /// <summary>The SVG's width attribute is its natural width; the inline style keeps it between that and 1.5 times it.</summary>
    private static void AssertFillsItsCard<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        var svg = cut.Find("svg.chart-svg");
        var natural = double.Parse(svg.GetAttribute("viewBox")!.Split(' ')[2], Invariant);
        var style = svg.GetAttribute("style")!;

        Assert.Equal(natural, double.Parse(svg.GetAttribute("width")!, Invariant), 2);
        Assert.Contains("width: 100%", style);
        Assert.Equal(natural, Px(style, "min-width"), 2);
        Assert.Equal(natural * 1.5, Px(style, "max-width"), 2);
    }

    private static double Px(string style, string property) =>
        double.Parse(Regex.Match(style, $@"{property}:\s*([\d.]+)px").Groups[1].Value, Invariant);
}
