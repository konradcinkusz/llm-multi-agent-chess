using Bunit;
using Chess.Web.Components.Charts;
using Xunit;
using static Chess.Web.Tests.Charts.ModelMapFixtures;

namespace Chess.Web.Tests.Charts;

public class ModelMapComponentTests
{
    private static readonly MapModel[] Field =
    [
        Model("alpha", rating: 1800, cost: 0.005, slot: 0),
        Model("beta", rating: 1200, cost: 0.06, slot: 1, provisional: true),
    ];

    [Fact]
    public void EveryModelIsABubbleLabelledWithItsName()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Field);

        Assert.Equal(["alpha", "beta"], cut.FindAll("g.model-map-mark").Select(g => g.GetAttribute("data-point")));
        Assert.Equal(["alpha", "beta"], cut.FindAll("text.chart-label").Select(t => t.TextContent));
    }

    [Fact]
    public void The_axes_default_to_cost_across_and_rating_up()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Field);

        Assert.Equal("Cost", cut.Find("#model-map-x option[selected]").GetAttribute("value"));
        Assert.Equal("Rating", cut.Find("#model-map-y option[selected]").GetAttribute("value"));
        Assert.Contains("Estimated cost per game (USD), log scale", AxisTitles(cut));
    }

    [Fact]
    public void Choosing_an_axis_redraws_the_map_with_its_title_and_quadrants()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Field);

        cut.Find("#model-map-x").Change("Legality");

        Assert.Contains("First-attempt legality", AxisTitles(cut));
        Assert.DoesNotContain("Estimated cost per game (USD), log scale", AxisTitles(cut));
        Assert.Contains(cut.FindAll("text.model-map-quadrant"), q => q.TextContent.Contains("reliable"));
        Assert.Equal("Legality", cut.Find("#model-map-x option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void Choosing_the_vertical_axis_moves_the_bubbles()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Field);
        var before = cut.Find("g[data-point='alpha'] circle.model-map-bubble").GetAttribute("cy");

        cut.Find("#model-map-y").Change("ArenaScore");

        Assert.NotEqual(before, cut.Find("g[data-point='alpha'] circle.model-map-bubble").GetAttribute("cy"));
        Assert.Equal("ArenaScore", cut.Find("#model-map-y option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void Move_quality_is_offered_only_when_stockfish_has_measured_the_moves()
    {
        using var ctx = new BunitContext();

        var without = Render(ctx, Field, moveQualityAvailable: false);
        Assert.DoesNotContain("MoveQuality", OptionValues(without, "model-map-x"));

        var with = Render(ctx, Field, moveQualityAvailable: true);
        Assert.Contains("MoveQuality", OptionValues(with, "model-map-x"));
    }

    [Fact]
    public void A_model_missing_one_axis_is_named_under_the_map_and_still_listed_in_the_table()
    {
        using var ctx = new BunitContext();
        var models = new[] { Field[0], Unpriced("random") };

        var cut = Render(ctx, models);

        Assert.Contains("random: no estimated cost per game (USD)", cut.Find(".model-map-unplaced").TextContent);
        Assert.Equal(2, cut.FindAll("details.chart-table tbody tr").Count);
    }

    [Fact]
    public void Without_a_model_on_both_axes_the_map_says_so()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, [Unpriced("random")]);

        Assert.Equal("No model has both of these coefficients yet.", cut.Find(".chart-empty").TextContent);
        Assert.Empty(cut.FindAll("svg circle.model-map-bubble"));
    }

    [Fact]
    public void A_provisional_model_is_hatched_and_an_established_one_is_solid()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Field);

        var alpha = cut.Find("g[data-point='alpha'] circle.model-map-bubble").GetAttribute("style");
        var beta = cut.Find("g[data-point='beta'] circle.model-map-bubble").GetAttribute("style");
        Assert.DoesNotContain("url(#", alpha);
        Assert.Contains("url(#", beta);
    }

    [Fact]
    public void The_hover_of_a_bubble_names_its_coefficients()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Field);

        Assert.Contains("rating 1800", cut.Find("g[data-point='alpha'] circle.model-map-bubble title").TextContent);
        Assert.Contains("estimated cost per game (USD) $0.005", cut.Find("g[data-point='alpha'] circle.model-map-bubble title").TextContent);
    }

    [Fact]
    public void The_reading_names_the_better_direction_of_both_axes()
    {
        using var ctx = new BunitContext();

        var cut = Render(ctx, Field);

        Assert.Equal("Up is better for rating; left is better for estimated cost per game (USD).",
            cut.Find(".model-map-reading").TextContent);
    }

    private static IRenderedComponent<ModelMap> Render(BunitContext ctx, IReadOnlyList<MapModel> models, bool moveQualityAvailable = false) =>
        ctx.Render<ModelMap>(parameters => parameters
            .Add(p => p.Title, "Two coefficients for every model")
            .Add(p => p.Models, models)
            .Add(p => p.MoveQualityAvailable, moveQualityAvailable));

    private static IEnumerable<string> AxisTitles(IRenderedComponent<ModelMap> cut) =>
        cut.FindAll("text.chart-axis-title").Select(t => t.TextContent);

    private static IEnumerable<string?> OptionValues(IRenderedComponent<ModelMap> cut, string selectId) =>
        cut.FindAll($"#{selectId} option").Select(o => o.GetAttribute("value"));
}
