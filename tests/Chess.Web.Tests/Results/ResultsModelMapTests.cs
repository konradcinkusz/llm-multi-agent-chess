using Chess.Web.Api;
using Chess.Web.Components.Charts;
using Chess.Web.Components.Pages.Results;
using Xunit;

namespace Chess.Web.Tests.Results;

public class ResultsModelMapTests
{
    [Fact]
    public void A_model_takes_its_leaderboard_numbers_and_its_breakdown_costs()
    {
        var model = new ModelRating("alpha", 1500, 1450, 1550, 30, 0.7, 0.98, 0.9, 0.95, 66, false, 60, 72);
        var results = new ModelResultsDto("alpha", 30, 20, 2, 8, 1, 400, 0.98, 1500, 900, 100, 22.1, 0.02, 0.033);

        var map = ResultsModel.MapModelOf(model, results, new Dictionary<string, int> { ["alpha"] = 3 });

        Assert.Equal("alpha", map.Model);
        Assert.Equal(3, map.Slot);
        Assert.Equal(30, map.Games);
        Assert.Equal(1500, map.Rating);
        Assert.Equal((1450, 1550), (map.RatingLow, map.RatingHigh));
        Assert.Equal(66, map.ArenaScore);
        Assert.Equal((60, 72), (map.ArenaScoreLow, map.ArenaScoreHigh));
        Assert.Equal(0.98, map.Legality);
        Assert.Equal(0.9, map.MoveQuality);
        Assert.Equal(1.5, map.LatencySeconds);
        Assert.Equal(1000, map.Tokens);
        Assert.Equal(0.02, map.CostPerGameUsd);
        Assert.Equal(0.033, map.ForfeitRate);
    }

    [Fact]
    public void Tokens_are_missing_unless_both_input_and_output_were_recorded()
    {
        var model = new ModelRating("alpha", 1500, 1450, 1550, 30, 0.7, 0.98, null, 0.95, 66, false);
        var results = new ModelResultsDto("alpha", 30, 20, 2, 8, 1, 400, 0.98, 1500, 900, null, null);

        var map = ResultsModel.MapModelOf(model, results, new Dictionary<string, int>());

        Assert.Null(map.Tokens);
        Assert.Null(map.Slot);
        Assert.Null(map.CostPerGameUsd);
    }

    [Fact]
    public void A_model_with_no_breakdown_row_has_no_latency_cost_or_tokens()
    {
        var model = new ModelRating("alpha", 1500, 1450, 1550, 30, 0.7, 0.98, null, 0.95, 66, false);

        var map = ResultsModel.MapModelOf(model, null, new Dictionary<string, int>());

        Assert.Null(map.LatencySeconds);
        Assert.Null(map.Tokens);
        Assert.Null(map.CostPerGameUsd);
        Assert.Null(map.ForfeitRate);
    }
}
