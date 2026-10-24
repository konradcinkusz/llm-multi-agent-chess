using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class RankedBarChartTests
{
    private static readonly RankedBar[] Bars =
    [
        new("beta", 0.45, 0.38, 0.52),
        new("alpha", 0.71, 0.60, 0.80, Highlighted: true),
        new("gamma", 0.58, Provisional: true),
    ];

    [Fact]
    public void DrawsTheBestBarFirst()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Equal(new[] { "alpha", "gamma", "beta" }, cut.FindAll("g.chart-bar").Select(g => g.GetAttribute("data-bar")));
    }

    [Fact]
    public void TheHighlightedBarIsAccentAndTheOthersNeutral()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Contains("var(--chart-accent)", BarStyle(cut, "alpha"));
        Assert.Contains("var(--chart-neutral)", BarStyle(cut, "beta"));
    }

    [Fact]
    public void OnlyBarsWithAnIntervalGetAWhisker()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        // A whisker is one bar line and two end caps; alpha and beta have intervals, gamma does not.
        Assert.Equal(6, cut.FindAll("line.chart-whisker").Count);
        Assert.Empty(cut.Find("g[data-bar='gamma']").QuerySelectorAll("line.chart-whisker"));
    }

    [Fact]
    public void ProvisionalBarIsHatchedAndLabelled()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Contains("gamma (provisional)", cut.Find("g[data-bar='gamma']").TextContent);
        Assert.Contains("url(#", BarStyle(cut, "gamma"));
        Assert.DoesNotContain("url(#", BarStyle(cut, "beta"));
    }

    [Fact]
    public void EveryBarCarriesAHoverTooltip()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        var titles = cut.FindAll("g.chart-bar > path > title").Select(t => t.TextContent).ToList();
        Assert.Equal(3, titles.Count);
        Assert.Contains("alpha: 0.71 (interval 0.60–0.80)", titles);
        Assert.Contains("gamma: 0.58, provisional", titles);
    }

    [Fact]
    public void ValuesAreDirectLabelledAtTheBarEnd()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Equal(new[] { "0.71", "0.58", "0.45" }, cut.FindAll("text.chart-value").Select(t => t.TextContent));
    }

    [Fact]
    public void ASingleSeriesChartHasNoLegend()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Empty(cut.FindAll(".chart-legend"));
    }

    [Fact]
    public void TheTableViewCarriesEveryBar()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        var rows = cut.FindAll("table tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Equal("alpha", rows[0].QuerySelector("td")!.TextContent);
    }

    [Fact]
    public void TheTitleNamesTheFigure()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Bars);

        Assert.Equal("Arena score", cut.Find("figcaption").TextContent);
    }

    [Fact]
    public void NoBarsMeansNoSvg()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, []);

        Assert.Empty(cut.FindAll("svg"));
        Assert.Contains("No data.", cut.Markup);
    }

    private static IRenderedComponent<RankedBarChart> Render(BunitContext ctx, IReadOnlyList<RankedBar> bars) =>
        ctx.Render<RankedBarChart>(p => p
            .Add(b => b.Title, "Arena score")
            .Add(b => b.Items, bars));

    private static string BarStyle(IRenderedComponent<RankedBarChart> cut, string label) =>
        cut.Find($"g[data-bar='{label}'] > path").GetAttribute("style")!;
}
