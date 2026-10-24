using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class StackedBar100Tests
{
    private static readonly StackRow[] Rows =
    [
        new("A", [new StackSegment("win", 0, 1), new StackSegment("draw", 1, 3)]),
        new("B", [new StackSegment("win", 0, 2)]),
    ];

    [Fact]
    public void AdjacentSegmentsAreTwoPixelsApart()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var paths = cut.FindAll("svg > path");
        var winRight = double.Parse(Regex.Match(paths[0].GetAttribute("d")!, @"H([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var drawLeft = double.Parse(Regex.Match(paths[1].GetAttribute("d")!, @"^M([\d.]+),").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(2.0, drawLeft - winRight, 2);
    }

    [Fact]
    public void TheShareOfEachSegmentIsInItsTooltip()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var titles = cut.FindAll("svg > path > title").Select(t => t.TextContent).ToList();
        Assert.Contains("A · win: 25%", titles);
        Assert.Contains("A · draw: 75%", titles);
        Assert.Contains("B · win: 100%", titles);
    }

    [Fact]
    public void OnlyTheDataEndOfEachBarIsRounded()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        var paths = cut.FindAll("svg > path");
        Assert.DoesNotContain("Q", paths[0].GetAttribute("d"));
        Assert.Contains("Q", paths[1].GetAttribute("d"));
        Assert.Contains("Q", paths[2].GetAttribute("d"));
    }

    [Fact]
    public void TheLegendNamesEverySegmentOnceWhenThereAreTwoOrMore()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx);

        Assert.Equal(new[] { "win", "draw" }, cut.FindAll(".chart-legend li").Select(li => li.TextContent));
    }

    private static IRenderedComponent<StackedBar100> Render(BunitContext ctx) =>
        ctx.Render<StackedBar100>(p => p
            .Add(b => b.Title, "Results breakdown")
            .Add(b => b.Rows, Rows));
}
