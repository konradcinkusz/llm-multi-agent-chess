using Chess.Api.Broadcast;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence.Entities;
using Chess.Api.Results;
using Chess.Api.Tournaments;
using Xunit;
using static Chess.Api.Tests.Results.ResultsFixtures;

namespace Chess.Api.Tests.Usage;

/// <summary>Variants (model@effort) are priced with their base model's prices in every estimate and breakdown (03 §10.2).</summary>
public class VariantPricingTests
{
    private static readonly ModelPricingDto Prices = new(2.0, 0.0, null);
    private static readonly Guid Game1 = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    [Fact]
    public async Task A_results_breakdown_prices_a_variant_with_its_base_models_prices()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = NewContext(name))
        {
            seed.Games.Add(Game(Game1, "fake-a@high", "fake-b", "1-0", "checkmate", T0.AddMinutes(1)));
            // 500 000 input tokens at $2 per million is $1.00, which is 100 AI credits at $0.01.
            seed.MoveAttempts.Add(new MoveAttemptEntity
            {
                GameId = Game1, Ply = 1, Number = 1, Prompt = "p", Legal = true, LatencyMs = 1, CreatedAt = T0,
                InputTokens = 500_000, OutputTokens = 0,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var models = new[] { new ModelDto("fake-a", "Fake A", 0, true, Prices), new ModelDto("fake-b", "Fake B", 0, true, Prices) };
        var rows = await ResultsBreakdown.ReadAsync(db, null, models, 0.01, CancellationToken.None);

        var variant = Assert.Single(rows, r => r.Model == "fake-a@high");
        Assert.Equal(1.0, variant.AvgEstimatedUsdPerGame!.Value, 12);
    }

    [Fact]
    public void A_broadcast_estimate_for_a_variant_equals_the_estimate_of_its_base_model()
    {
        var inputs = new Dictionary<string, ModelCostInputs>
        {
            ["fake-a"] = new(3, Prices, new ModelTokenAverage(5, 1000, 200)),
        };

        var variant = BroadcastPlanner.EstimateFor("fake-a@high", 4, 80, inputs, 0.01);
        var plain = BroadcastPlanner.EstimateFor("fake-a", 4, 80, inputs, 0.01);

        Assert.NotNull(variant.EstimatedUsd);
        Assert.Equal(plain.EstimatedUsd, variant.EstimatedUsd);
        Assert.Equal(plain.EstimatedAiCredits, variant.EstimatedAiCredits);
        Assert.Equal(plain.EstimatedPremiumRequests, variant.EstimatedPremiumRequests);
        Assert.Equal(CostEstimator.HistoryBasis, variant.TokenBasis);
    }

    [Fact]
    public void A_variant_of_a_model_with_no_prices_stays_unknown()
    {
        var inputs = new Dictionary<string, ModelCostInputs> { ["fake-c"] = new(null, null, null) };

        var variant = BroadcastPlanner.EstimateFor("fake-c@low", 4, 80, inputs, 0.01);

        Assert.Equal(CostEstimator.UnknownBasis, variant.TokenBasis);
        Assert.Null(variant.EstimatedUsd);
    }
}
