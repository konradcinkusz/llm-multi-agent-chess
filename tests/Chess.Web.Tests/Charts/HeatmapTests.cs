using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class HeatmapTests
{
    private static readonly string[] Labels = ["alpha", "beta", "gamma"];

    private static readonly HeatCell[] Cells =
    [
        new("alpha", "beta", 0.5),
        new("beta", "alpha", 0.8),
        new("alpha", "gamma", 0.2, Muted: true),
        new("beta", "gamma", null),
    ];

    [Fact]
    public void TheMidpointIsTheNeutralGrey()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal("fill: var(--chart-diverge-mid)", CellRect(cut, "alpha", "beta").GetAttribute("style"));
    }

    [Fact]
    public void AboveTheMidpointMixesInTheHighPole()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        // 0.8 is 0.3 of the way from 0.5 to the maximum 1.0 of the 0.5 span, i.e. 60 %.
        Assert.Equal("fill: color-mix(in srgb, var(--chart-diverge-high) 60%, var(--chart-diverge-mid))",
            CellRect(cut, "beta", "alpha").GetAttribute("style"));
    }

    [Fact]
    public void BelowTheMidpointMixesInTheLowPole()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal("fill: color-mix(in srgb, var(--chart-diverge-low) 60%, var(--chart-diverge-mid))",
            CellRect(cut, "alpha", "gamma").GetAttribute("style"));
    }

    [Fact]
    public void MutedCellsAreDimmedAndSaySoInTheirTooltip()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var rect = CellRect(cut, "alpha", "gamma");
        Assert.Contains("chart-muted-cell", rect.ClassName);
        Assert.Equal("alpha vs gamma: 0.20 (not significant)", rect.QuerySelector("title")!.TextContent);
        Assert.DoesNotContain("chart-muted-cell", CellRect(cut, "alpha", "beta").ClassName ?? "");
    }

    [Fact]
    public void CellsCarryTheirValueAsText()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal(new[] { "0.50", "0.20", "0.80" }, cut.FindAll("text.chart-value").Select(t => t.TextContent));
    }

    [Fact]
    public void MissingPairsAndEmptyCellsAreHoles()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        // Diagonal and unsampled pairs are not in the input; beta vs gamma is present with no value.
        Assert.Equal(6, cut.FindAll("rect.chart-hole").Count);
    }

    [Fact]
    public void EveryValuedCellHasATooltip()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal(3, cut.FindAll("g.chart-cell > rect > title").Count);
    }

    private static IRenderedComponent<Heatmap> Render(BunitContext ctx) =>
        ctx.Render<Heatmap>(p => p
            .Add(b => b.Title, "Head-to-head RQC")
            .Add(b => b.Labels, Labels)
            .Add(b => b.Cells, Cells));

    private static AngleSharp.Dom.IElement CellRect(IRenderedComponent<Heatmap> cut, string row, string column) =>
        cut.Find($"g.chart-cell[data-row='{row}'][data-column='{column}'] > rect");
}
