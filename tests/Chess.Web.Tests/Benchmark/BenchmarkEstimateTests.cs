using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkEstimateTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<ModelDto> Models = JsonSerializer
        .Deserialize<List<ModelDto>>(Fixtures.Models, Options)!;

    private static readonly CostInputsDto Costs = JsonSerializer
        .Deserialize<CostInputsDto>(Fixtures.CostInputs, Options)!;

    private static ModelDto Model(string id) => Models.Single(model => model.Id == id);

    [Fact]
    public void TheCandidatePlaysEveryGameAndEachOpponentPlaysItsOwn()
    {
        var opponents = new[] { Model("fake-b"), Model("random") };

        var result = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high", opponents, Costs, 2);

        Assert.Equal(4, result.Totals.Games);
        Assert.Equal(new[] { "candidate", "opponent", "opponent" }, result.Rows.Select(row => row.Role).ToArray());
        Assert.Equal(new[] { 4, 2, 2 }, result.Rows.Select(row => row.Games).ToArray());
        Assert.Equal("fake-a@high", result.Rows[0].Player);
    }

    [Fact]
    public void TheCostIsPricedAtTheMovesEachSideMakesAndTheRandomPlayerIsFree()
    {
        // 0.0045 USD a move (1 000 input and 100 output tokens at 3 and 15 USD a million); 40 moves a game.
        var result = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high", new[] { Model("fake-b"), Model("random") }, Costs, 2);

        Assert.Equal(0.72, result.Rows[0].Usd!.Value, precision: 6);
        Assert.Equal(0.36, result.Rows[1].Usd!.Value, precision: 6);
        Assert.Equal(0, result.Rows[2].Usd!.Value);
        Assert.Equal(1.08, result.Totals.Usd, precision: 6);
        Assert.Equal(0, result.Totals.UnknownUsd);
        Assert.Equal("assumed", result.Rows[0].Basis);
    }

    [Fact]
    public void PremiumRequestsFollowTheMultiplierOfEachSideAndAnUnknownOneIsNotCounted()
    {
        var result = BenchmarkEstimate.For(Model("gpt"), "gpt", new[] { Model("local"), Model("fake-a") }, Costs, 2);

        // The candidate plays 4 games at 40 requests and multiplier 1; the custom model's multiplier is unknown.
        Assert.Equal(160, result.Rows[0].PremiumRequests!.Value);
        Assert.Null(result.Rows[1].PremiumRequests);
        Assert.Equal(1, result.Totals.UnknownPremium);
        Assert.Equal(160, result.Totals.PremiumRequests);
    }

    [Fact]
    public void AModelWithoutPricesIsUnknownAndTheKnownTotalIsALowerBound()
    {
        // The candidate's 2 games (80 moves at 0.0045 USD) are priced; the custom model's moves are not.
        var result = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high", new[] { Model("local") }, Costs, 2);

        Assert.Null(result.Rows[1].Usd);
        Assert.Equal("unknown", result.Rows[1].Basis);
        Assert.Equal(0.36, result.Totals.Usd, precision: 6);
        Assert.Equal(1, result.Totals.UnknownUsd);
    }

    [Fact]
    public void WithoutCostInputsNoMoneyIsKnownAndEveryRowIsUnknown()
    {
        var result = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high", new[] { Model("fake-b") }, null, 2);

        Assert.All(result.Rows, row => Assert.Null(row.Usd));
        Assert.Equal(0, result.Totals.Usd);
        Assert.Equal(result.Rows.Count, result.Totals.UnknownUsd);
    }

    [Fact]
    public void ARunOfAtLeastOneHundredAndFiftyGamesIsLarge()
    {
        var small = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high", new[] { Model("random") }, Costs, 2).Totals;
        var many = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high",
            Enumerable.Repeat(Model("random"), 5).ToArray(), Costs, 30).Totals;

        Assert.False(BenchmarkEstimate.IsLarge(small));
        Assert.Equal(150, many.Games);
        Assert.True(BenchmarkEstimate.IsLarge(many));
    }

    [Fact]
    public void ARunWhoseKnownCostReachesFiveDollarsIsLargeWhateverItsGameCount()
    {
        // 120 games: the candidate's 4 800 moves at 0.0045 USD are 21.60 USD, though 120 games is below the game threshold.
        // The custom model has no price, so the known total is a lower bound; it is still large.
        var expensive = BenchmarkEstimate.For(Model("fake-a"), "fake-a@high",
            new[] { Model("fake-b"), Model("random"), Model("local"), Model("gpt") }, Costs, 30).Totals;

        Assert.True(expensive.Games < BenchmarkEstimate.LargeGames);
        Assert.True(BenchmarkEstimate.IsLarge(expensive));
    }
}
