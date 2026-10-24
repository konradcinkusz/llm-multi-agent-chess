using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class PremiumEstimateTests
{
    private static readonly IReadOnlyList<ModelDto> Catalog =
    [
        new("gpt", "GPT", 1, true),
        new("claude", "Claude", 0.33, true),
        new("random", "Random", null, true),
    ];

    [Fact]
    public void EachModelPlaysEveryOtherModelGamesPerPairTimesAtEightyRequestsAGame()
    {
        // Three models, four games per pair: each model meets two others four times, so eight games.
        var estimates = PremiumEstimate.For(Catalog, ["gpt", "claude", "random"], 4);

        Assert.Equal(new PremiumEstimate.ModelEstimate("gpt", 8 * 80 * 1), estimates[0]);
        Assert.Equal(8 * 80 * 0.33, estimates[1].Requests!.Value, 6);
    }

    [Fact]
    public void TwoModelsWithTwoGamesPerPairCostTwoGamesEach()
    {
        var estimates = PremiumEstimate.For(Catalog, ["gpt", "claude"], 2);

        Assert.Equal(160, estimates[0].Requests);
        Assert.Equal(2 * 80 * 0.33, estimates[1].Requests!.Value, 6);
    }

    [Fact]
    public void AModelWithoutAKnownMultiplierIsUnknownAndLeftOutOfTheTotal()
    {
        var estimates = PremiumEstimate.For(Catalog, ["gpt", "random"], 2);

        Assert.Null(estimates[1].Requests);
        Assert.Equal(160, PremiumEstimate.Known(estimates));
    }

    [Fact]
    public void AModelMissingFromTheCatalogIsUnknown()
    {
        var estimates = PremiumEstimate.For(Catalog, ["gpt", "not-listed"], 2);

        Assert.Null(estimates[1].Requests);
    }
}
