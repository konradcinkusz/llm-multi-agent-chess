using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class PlanTimelineTests
{
    private static readonly DateTimeOffset Starts = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ends = Starts.AddHours(24);

    [Fact]
    public void DrawsOneGameForEachSlotWithWhiteOnTheLeftAndBlackOnTheRight()
    {
        var slots = new List<SlotEdit>
        {
            new(new PlayerSpec("llm", "gpt"), new PlayerSpec("llm", "claude"), Starts, Starts.AddHours(1)),
            new(new PlayerSpec("llm", "claude"), new PlayerSpec("llm", "gpt"), Starts.AddHours(2), Starts.AddHours(3)),
        };
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlanTimeline>(parameters => parameters
            .Add(p => p.Slots, slots).Add(p => p.StartsAt, Starts).Add(p => p.EndsAt, Ends));

        var games = cut.FindAll("[data-testid='plan-preview'] .plan-game");
        Assert.Equal(2, games.Count);
        var halves = games[0].QuerySelectorAll("rect");
        Assert.Equal(2, halves.Length);
        // The colour goes in a style: a var() in an SVG fill attribute is not resolved, and the bar would paint black.
        Assert.Equal($"fill: {ChartColour("gpt")}", halves[0].GetAttribute("style"));
        Assert.Equal($"fill: {ChartColour("claude")}", halves[1].GetAttribute("style"));
        Assert.True(double.Parse(halves[0].GetAttribute("x")!) < double.Parse(halves[1].GetAttribute("x")!));
    }

    [Fact]
    public void PlacesEachGameByItsTimeOnTheWindow()
    {
        var slots = new List<SlotEdit> { new(new PlayerSpec("llm", "gpt"), new PlayerSpec("llm", "claude"), Starts.AddHours(12), Starts.AddHours(13)) };
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlanTimeline>(parameters => parameters
            .Add(p => p.Slots, slots).Add(p => p.StartsAt, Starts).Add(p => p.EndsAt, Ends));

        var first = cut.Find(".plan-game rect");
        Assert.Equal(500d, double.Parse(first.GetAttribute("x")!), precision: 6);
    }

    [Fact]
    public void LabelsTheHoursAndLegendsEveryModelOnce()
    {
        var slots = new List<SlotEdit> { new(new PlayerSpec("llm", "gpt"), new PlayerSpec("random", "random"), Starts, Starts.AddHours(1)) };
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlanTimeline>(parameters => parameters
            .Add(p => p.Slots, slots).Add(p => p.StartsAt, Starts).Add(p => p.EndsAt, Ends));

        Assert.Contains("10:00", cut.Markup);
        Assert.Equal(2, cut.FindAll(".plan-legend li").Count);
    }

    [Fact]
    public void AnEmptyPlanDrawsAnAxisAndNoGames()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<PlanTimeline>(parameters => parameters
            .Add(p => p.Slots, new List<SlotEdit>()).Add(p => p.StartsAt, Starts).Add(p => p.EndsAt, Ends));

        Assert.Empty(cut.FindAll(".plan-game"));
        Assert.NotEmpty(cut.FindAll(".plan-axis line"));
    }

    private static string ChartColour(string model) => model switch
    {
        // ChartPalette assigns slots by alphabetical position: claude comes before gpt.
        "claude" => "var(--chart-series-0)",
        "gpt" => "var(--chart-series-1)",
        _ => "var(--chart-neutral)",
    };
}
