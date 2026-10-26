using Chess.Api.Contracts;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

/// <summary>The token history of a styled player id: its own moves first, then its base model's (03 §14.4).</summary>
public class CostInputsHistoryTests
{
    private static readonly ModelPricingDto Pricing = new(1.0, 4.0, null);
    private static readonly ModelCostInputs ModelInputs = new(null, Pricing, null);

    [Fact]
    public void TheFullIdsOwnHistoryIsUsedWhenItHasMoves()
    {
        var history = new Dictionary<string, ModelTokenAverage>
        {
            ["fake-a~think"] = new(3, 800, 200),
            ["fake-a"] = new(10, 100, 50),
        };

        var inputs = CostInputs.For("fake-a~think", ModelInputs, history);
        var cost = CostEstimator.Estimate("fake-a", 40, inputs, usdPerAiCredit: 0.01);

        Assert.Equal(new ModelTokenAverage(3, 800, 200), inputs.History);
        Assert.Equal(CostEstimator.HistoryBasis, cost.Basis);
        Assert.Equal(3, cost.SampleMoves);
    }

    [Fact]
    public void AStyledIdWithNoMovesFallsBackToItsBaseModelsHistory()
    {
        var history = new Dictionary<string, ModelTokenAverage> { ["fake-a"] = new(10, 100, 50) };

        var inputs = CostInputs.For("fake-a~board", ModelInputs, history);
        var cost = CostEstimator.Estimate("fake-a", 40, inputs, usdPerAiCredit: 0.01);

        Assert.Equal(new ModelTokenAverage(10, 100, 50), inputs.History);
        Assert.Equal(CostEstimator.HistoryBasis, cost.Basis);
        Assert.Equal(10, cost.SampleMoves);
    }

    [Fact]
    public void WithNoHistoryAtAllTheTokensAreAssumed()
    {
        var history = new Dictionary<string, ModelTokenAverage>();

        var inputs = CostInputs.For("fake-a~think", ModelInputs, history);
        var cost = CostEstimator.Estimate("fake-a", 40, inputs, usdPerAiCredit: 0.01);

        Assert.Null(inputs.History);
        Assert.Equal(CostEstimator.AssumedBasis, cost.Basis);
        Assert.Null(cost.SampleMoves);
    }

    [Fact]
    public void PricingAlwaysComesFromTheBaseModel()
    {
        var history = new Dictionary<string, ModelTokenAverage> { ["fake-a~think"] = new(3, 800, 200) };

        var inputs = CostInputs.For("fake-a~think", ModelInputs, history);

        Assert.Same(Pricing, inputs.Pricing);
    }
}
