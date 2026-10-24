using System.Globalization;
using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class ScatterPlotTests
{
    // alpha and beta sit on the same spot, so their labels must be pushed apart.
    private static readonly ScatterPoint[] Points =
    [
        new("alpha", 1.2, 1510),
        new("beta", 1.2, 1510),
        new("gamma", 3, 1450),
    ];

    [Fact]
    public void EveryPointIsDirectLabelled()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, cut.FindAll("text.chart-label").Select(t => t.TextContent));
    }

    [Fact]
    public void CoincidentLabelsDoNotOverlap()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var ys = cut.FindAll("text.chart-label")
            .Select(t => double.Parse(t.GetAttribute("y")!, CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(ys[1] - ys[0] >= 14 - 0.001, $"labels {ys[0]} and {ys[1]} overlap");
    }

    [Fact]
    public void EveryPointHasAHoverTooltip()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var titles = cut.FindAll("circle.chart-hit > title").Select(t => t.TextContent).ToList();
        Assert.Equal(3, titles.Count);
        Assert.Contains("alpha: 1.2, 1510", titles);
    }

    [Fact]
    public void AScatterHasNoLegend()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Empty(cut.FindAll(".chart-legend"));
    }

    private static IRenderedComponent<ScatterPlot> Render(BunitContext ctx) =>
        ctx.Render<ScatterPlot>(p => p
            .Add(b => b.Title, "Strength vs cost")
            .Add(b => b.XTitle, "Average latency (s)")
            .Add(b => b.YTitle, "Rating")
            .Add(b => b.Points, Points));
}
