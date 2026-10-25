using Chess.Web.Components.Charts;
using Xunit;
using static Chess.Web.Tests.Charts.ModelMapFixtures;
using static Chess.Web.Components.Charts.ModelMapGeometry;

namespace Chess.Web.Tests.Charts;

public class ModelMapGeometryTests
{
    // The width of one character of chart text (ChartGeometry.LabelCharWidth, which tests cannot reach).
    private const double LabelCharWidth = 7.2;

    [Fact]
    public void A_cost_range_over_ten_times_is_drawn_on_a_log_scale()
    {
        // 0.001, 0.01 and 0.1 are equally spaced in log units: a linear axis would put the second one near the first.
        var models = new[]
        {
            Model("cheap", cost: 0.001),
            Model("middle", cost: 0.01),
            Model("dear", cost: 0.1),
        };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        var x = XOf(view);
        Assert.Equal(x["middle"] - x["cheap"], x["dear"] - x["middle"], 6);
        Assert.Contains("log scale", view.XTitle);
    }

    [Fact]
    public void Costs_that_differ_only_in_the_third_figure_are_drawn_apart_and_labelled_distinctly()
    {
        // The fake gateway's models cost about $0.00014 per game and differ by under 1 %: each bubble needs its own x.
        var models = new[]
        {
            Model("fake-a", cost: 0.00014066625),
            Model("fake-b", cost: 0.00014157625),
        };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        var x = XOf(view);
        Assert.True(Math.Abs(x["fake-a"] - x["fake-b"]) > 100, "the two bubbles are drawn on top of each other");
        Assert.Equal(view.XTicks.Select(t => t.Text).Distinct().Count(), view.XTicks.Count);
    }

    [Fact]
    public void A_range_of_exactly_ten_times_stays_linear()
    {
        // 1, 2 and 10 are not spaced evenly on a log axis; on a linear one the gaps are 1 and 8.
        var models = new[]
        {
            Model("one", cost: null, latency: 1),
            Model("two", cost: null, latency: 2),
            Model("ten", cost: null, latency: 10),
        };

        var view = Build(models, MapCoefficient.Latency, MapCoefficient.Rating);

        var x = XOf(view);
        Assert.Equal(8, (x["ten"] - x["two"]) / (x["two"] - x["one"]), 6);
        DoesNotContainLog(view.XTitle);
    }

    [Fact]
    public void A_value_of_zero_keeps_an_axis_linear_because_no_log_of_zero_exists()
    {
        // Random's latency is zero; with it on the axis the range is not a log range at all.
        var models = new[]
        {
            Model("random", latency: 0),
            Model("slow", latency: 1),
            Model("slower", latency: 1000),
        };

        var view = Build(models, MapCoefficient.Latency, MapCoefficient.Rating);

        DoesNotContainLog(view.XTitle);
        Assert.Equal(3, view.Marks.Count);
    }

    [Fact]
    public void Bubble_area_is_proportional_to_the_rated_games()
    {
        var models = new[]
        {
            Model("many", games: 16, cost: 0.01),
            Model("few", games: 4, cost: 0.02),
        };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        var radius = view.Marks.ToDictionary(m => m.Model, m => m.Radius);
        var areaRatio = (radius["many"] * radius["many"]) / (radius["few"] * radius["few"]);
        Assert.Equal(4, areaRatio, 6);
    }

    [Fact]
    public void The_median_guides_sit_on_the_median_model_of_each_axis()
    {
        // The median of the costs (0.02) and of the ratings (1400) is the middle model, not the mean or the extremes: the
        // costs are skewed, so the mean (0.027) would sit well away from it.
        var models = new[]
        {
            Model("low", rating: 1000, cost: 0.01),
            Model("mid", rating: 1400, cost: 0.02),
            Model("high", rating: 2000, cost: 0.05),
        };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        var mid = view.Marks.Single(m => m.Model == "mid");
        Assert.Equal(mid.X, view.MedianX!.Value, 6);
        Assert.Equal(mid.Y, view.MedianY!.Value, 6);
    }

    [Fact]
    public void The_default_quadrants_read_cheap_and_strong_from_the_better_directions()
    {
        // x = cost (lower is better, so the left end is the good one); y = rating (higher is better, so the top is).
        var view = Build(Field(), MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Equal("strong and cheap", QuadrantAt(view, left: true, top: true));
        Assert.Equal("strong but expensive", QuadrantAt(view, left: false, top: true));
        Assert.Equal("weak but cheap", QuadrantAt(view, left: true, top: false));
        Assert.Equal("weak and expensive", QuadrantAt(view, left: false, top: false));
    }

    [Fact]
    public void Each_axis_direction_changes_the_quadrant_names()
    {
        // x = legality (higher is better, so the right end is reliable); y = cost (lower is better, so the top is dear).
        var view = Build(Field(), MapCoefficient.Legality, MapCoefficient.Cost);

        Assert.Equal("expensive and error-prone", QuadrantAt(view, left: true, top: true));
        Assert.Equal("cheap and reliable", QuadrantAt(view, left: false, top: false));
    }

    [Fact]
    public void The_reading_says_which_way_is_better_on_each_axis()
    {
        var view = Build(Field(), MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Equal("Up is better for rating; left is better for estimated cost per game (USD).", view.Reading);
    }

    [Fact]
    public void A_degenerate_axis_still_plots_every_model_with_one_tick()
    {
        // Every model costs the same: the x axis has no spread, so all bubbles share one x and the axis shows one tick.
        var models = new[]
        {
            Model("a", rating: 1000, cost: 0.02),
            Model("b", rating: 1500, cost: 0.02),
            Model("c", rating: 2000, cost: 0.02),
        };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Equal(3, view.Marks.Count);
        Assert.All(view.Marks, mark => Assert.Equal(view.Marks[0].X, mark.X, 6));
        Assert.InRange(view.Marks[0].X, PlotLeft, PlotRight);
        Assert.Single(view.XTicks);
    }

    [Fact]
    public void Labels_stay_inside_the_plot_even_when_models_coincide()
    {
        // Twelve models on one spot, and one at the far right: the labels are pushed apart, but never off the plot.
        var models = Enumerable.Range(1, 12)
            .Select(i => Model($"model-{i:00}", rating: 1500, cost: 0.01))
            .Append(Model("far-right-model", rating: 1500, cost: 0.03))
            .ToList();

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.All(view.Marks, mark =>
        {
            Assert.InRange(mark.LabelY, PlotTop, PlotBottom);
            if (mark.LabelAnchor == "start")
            {
                Assert.True(mark.LabelX + mark.Model.Length * LabelCharWidth <= PlotRight + 0.001,
                    $"label of {mark.Model} runs off the right edge");
            }
            else
            {
                Assert.True(mark.LabelX - mark.Model.Length * LabelCharWidth >= PlotLeft - 0.001,
                    $"label of {mark.Model} runs off the left edge");
            }
        });
        Assert.Equal("end", view.Marks.Single(m => m.Model == "far-right-model").LabelAnchor);
    }

    [Fact]
    public void A_model_without_one_of_the_two_coefficients_is_not_placed_and_is_named()
    {
        var models = new[] { Model("priced"), Unpriced("random") };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Equal(["priced"], view.Marks.Select(m => m.Model));
        Assert.Equal(["random: no estimated cost per game (USD)"], view.Unplaced);
    }

    [Fact]
    public void Ratings_and_arena_scores_draw_their_interval_as_a_whisker_and_nothing_else_does()
    {
        var models = new[] { Model("a", rating: 1500, cost: 0.01) };

        var onRating = Build(models, MapCoefficient.Rating, MapCoefficient.ArenaScore).Marks.Single();
        var onCost = Build(models, MapCoefficient.Cost, MapCoefficient.Rating).Marks.Single();

        Assert.NotNull(onRating.WhiskerX);
        Assert.True(onRating.WhiskerX!.Value.To > onRating.WhiskerX.Value.From);
        Assert.NotNull(onRating.WhiskerY);
        Assert.Null(onCost.WhiskerX);
    }

    [Fact]
    public void The_hover_text_names_every_coefficient_and_the_games_behind_them()
    {
        var models = new[] { Model("alpha", games: 30, rating: 1500, cost: 0.0105) };

        var tooltip = Build(models, MapCoefficient.Cost, MapCoefficient.Rating).Marks.Single().Tooltip;

        Assert.StartsWith("alpha: rating 1500 (1450 to 1550)", tooltip);
        Assert.Contains("estimated cost per game (USD) $0.0105", tooltip);
        Assert.Contains("first-attempt legality 90%", tooltip);
        Assert.EndsWith("30 rated games", tooltip);
    }

    [Fact]
    public void The_table_lists_every_model_and_every_coefficient_including_the_unplaced_ones()
    {
        var models = new[] { Model("alpha", cost: 0.01), Unpriced("random") };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Contains("Rating", view.TableHeaders);
        Assert.Contains("Estimated cost per game (USD)", view.TableHeaders);
        Assert.Contains("Rated games", view.TableHeaders);
        Assert.Equal(2, view.TableRows.Count);
        Assert.Equal("random", view.TableRows[1][0]);
        Assert.Equal("—", view.TableRows[1][view.TableHeaders.ToList().IndexOf("Estimated cost per game (USD)")]);
    }

    [Fact]
    public void With_nothing_placed_the_map_has_no_marks_and_still_names_the_models()
    {
        var models = new[] { Unpriced("random") };

        var view = Build(models, MapCoefficient.Cost, MapCoefficient.Rating);

        Assert.Empty(view.Marks);
        Assert.Empty(view.Quadrants);
        Assert.Equal(["random: no estimated cost per game (USD)"], view.Unplaced);
        Assert.Single(view.TableRows);
    }

    private static IReadOnlyList<MapModel> Field() =>
    [
        Model("cheap-strong", rating: 1800, cost: 0.005),
        Model("dear-strong", rating: 1700, cost: 0.05),
        Model("cheap-weak", rating: 1200, cost: 0.004, legality: 0.7),
        Model("dear-weak", rating: 1100, cost: 0.06, legality: 0.6),
    ];

    private static ModelMapView Build(IReadOnlyList<MapModel> models, MapCoefficient x, MapCoefficient y) =>
        ModelMapGeometry.Build(models, x, y);

    private static Dictionary<string, double> XOf(ModelMapView view) =>
        view.Marks.ToDictionary(m => m.Model, m => m.X);

    private static string QuadrantAt(ModelMapView view, bool left, bool top)
    {
        var x = left ? PlotLeft + 8 : PlotRight - 8;
        var y = top ? PlotTop + 16 : PlotBottom - 8;
        return view.Quadrants.Single(q => Math.Abs(q.X - x) < 0.001 && Math.Abs(q.Y - y) < 0.001).Name;
    }

    private static void DoesNotContainLog(string title) =>
        Assert.DoesNotContain("log scale", title);
}
