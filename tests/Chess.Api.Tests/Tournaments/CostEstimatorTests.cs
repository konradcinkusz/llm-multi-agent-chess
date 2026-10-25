using Chess.Api.Contracts;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

/// <summary>The cost estimate of 03 §10.2 on canned prices: no gateway, no database.</summary>
public class CostEstimatorTests
{
    // Canned prices in USD per 1M tokens: 3 in, 15 out. At 0.01 USD a credit that is 300 and 1 500 credits per 1M.
    private static readonly ModelPricingDto Priced = new(3.0, 15.0, 0.3);

    private static readonly CostEstimateOptions Options = new()
    {
        UsdPerAiCredit = 0.01,
        AssumedInputTokensPerMove = 700,
        AssumedOutputTokensPerMove = 300,
    };

    [Fact]
    public void Tokens_come_from_stored_moves_when_there_are_some()
    {
        // 40 moves × (1 000 in × $3 + 200 out × $15) / 1M = 40 × $6 000 / 1M = $0.24 = 24 credits.
        var inputs = new ModelCostInputs(null, Priced, new ModelTokenAverage(Moves: 12, AverageInputTokens: 1000, AverageOutputTokens: 200));

        var cost = CostEstimator.Estimate("fake-a", 40, inputs, Options);

        Assert.Equal(CostEstimator.HistoryBasis, cost.Basis);
        Assert.Equal(12, cost.SampleMoves);
        Assert.Equal(0.24, cost.Usd!.Value, precision: 10);
        Assert.Equal(24, cost.AiCredits!.Value, precision: 9);
    }

    [Fact]
    public void Tokens_are_assumed_when_a_model_has_no_stored_moves()
    {
        // 40 moves × (700 in × $3 + 300 out × $15) / 1M = 40 × $6 600 / 1M = $0.264 = 26.4 credits.
        var inputs = new ModelCostInputs(null, Priced, History: null);

        var cost = CostEstimator.Estimate("fake-a", 40, inputs, Options);

        Assert.Equal(CostEstimator.AssumedBasis, cost.Basis);
        Assert.Null(cost.SampleMoves);
        Assert.Equal(0.264, cost.Usd!.Value, precision: 10);
        Assert.Equal(26.4, cost.AiCredits!.Value, precision: 9);
    }

    [Fact]
    public void A_history_with_no_moves_counts_as_none()
    {
        var inputs = new ModelCostInputs(null, Priced, new ModelTokenAverage(Moves: 0, AverageInputTokens: 0, AverageOutputTokens: 0));

        var cost = CostEstimator.Estimate("fake-a", 40, inputs, Options);

        Assert.Equal(CostEstimator.AssumedBasis, cost.Basis);
        Assert.Null(cost.SampleMoves);
    }

    [Fact]
    public void A_model_without_both_prices_is_unknown_and_its_credits_are_not_guessed()
    {
        var noPrices = new ModelCostInputs(3, null, null);
        var oneSide = new ModelCostInputs(null, new ModelPricingDto(3.0, null, null), null);

        foreach (var inputs in new[] { noPrices, oneSide })
        {
            var cost = CostEstimator.Estimate("fake-a", 40, inputs, Options);

            Assert.Equal(CostEstimator.UnknownBasis, cost.Basis);
            Assert.Null(cost.AiCredits);
            Assert.Null(cost.Usd);
        }
    }

    [Fact]
    public void A_model_that_is_not_listed_is_unknown()
    {
        var cost = CostEstimator.Estimate("fake-nope", 40, inputs: null, Options);

        Assert.Equal(CostEstimator.UnknownBasis, cost.Basis);
        Assert.Null(cost.AiCredits);
    }

    [Fact]
    public void The_random_player_is_free_whatever_its_moves()
    {
        var cost = CostEstimator.Estimate("random", 1000, inputs: null, Options);

        Assert.Equal(CostEstimator.FreeBasis, cost.Basis);
        Assert.Equal(0, cost.AiCredits);
        Assert.Equal(0, cost.Usd);
    }

    [Fact]
    public void The_rate_changes_credits_but_not_the_dollars()
    {
        var inputs = new ModelCostInputs(null, Priced, null);

        var cheap = CostEstimator.Estimate("fake-a", 40, inputs, new CostEstimateOptions { UsdPerAiCredit = 0.02 });
        var dear = CostEstimator.Estimate("fake-a", 40, inputs, Options);

        Assert.Equal(dear.Usd, cheap.Usd);
        Assert.Equal(dear.AiCredits!.Value / 2, cheap.AiCredits!.Value, precision: 9);
    }

    [Fact]
    public void The_premium_multiplier_does_not_enter_the_credit_estimate()
    {
        // A multiplier model with prices is priced by its tokens; the multiplier is a separate, premium-request figure.
        var withMultiplier = new ModelCostInputs(3, Priced, null);
        var without = new ModelCostInputs(null, Priced, null);

        Assert.Equal(
            CostEstimator.Estimate("fake-a", 40, without, Options),
            CostEstimator.Estimate("fake-a", 40, withMultiplier, Options));
    }
}
