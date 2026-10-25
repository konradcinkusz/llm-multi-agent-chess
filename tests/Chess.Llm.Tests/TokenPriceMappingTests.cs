using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class TokenPriceMappingTests
{
    [Fact]
    public void AiCreditPricesAreKeptAsTheyAre()
    {
        var prices = TokenPriceMapping.Map(1_000_000, 1.25, 5.0, 0.125, null, pricesInNanoAiu: false);

        Assert.Equal(new LlmTokenPrices(1_000_000, 1.25, 5.0, 0.125), prices);
    }

    [Fact]
    public void NanoAiuPricesAreDividedByOneBillionToGetCredits()
    {
        var prices = TokenPriceMapping.Map(1_000_000, 1_250_000_000, 5_000_000_000, 125_000_000, null, pricesInNanoAiu: true);

        Assert.Equal(new LlmTokenPrices(1_000_000, 1.25, 5.0, 0.125), prices);
    }

    [Fact]
    public void CacheReadPriceIsPreferredToTheDeprecatedCachePrice()
    {
        var prices = TokenPriceMapping.Map(1_000_000, 1.0, 4.0, 0.1, 9.9, pricesInNanoAiu: false);

        Assert.Equal(0.1, prices!.CachedInputCredits);
    }

    [Fact]
    public void CachePriceIsUsedWhenTheCacheReadPriceIsAbsent()
    {
        var prices = TokenPriceMapping.Map(1_000_000, 1.0, 4.0, null, 0.2, pricesInNanoAiu: false);

        Assert.Equal(0.2, prices!.CachedInputCredits);
    }

    [Fact]
    public void APartlyPricedModelKeepsThePricesItHas()
    {
        var prices = TokenPriceMapping.Map(1_000_000, null, 4.0, null, null, pricesInNanoAiu: false);

        Assert.Equal(new LlmTokenPrices(1_000_000, null, 4.0, null), prices);
    }

    [Fact]
    public void NoUnitMeansNoPricesRatherThanAGuess()
    {
        Assert.Null(TokenPriceMapping.Map(1_000_000, 1.0, 4.0, 0.1, null, pricesInNanoAiu: null));
    }

    [Fact]
    public void NoBatchSizeOrAZeroBatchSizeMeansNoPrices()
    {
        Assert.Null(TokenPriceMapping.Map(null, 1.0, 4.0, 0.1, null, pricesInNanoAiu: false));
        Assert.Null(TokenPriceMapping.Map(0, 1.0, 4.0, 0.1, null, pricesInNanoAiu: false));
    }

    [Fact]
    public void AModelWithNoPriceAtAllHasNoPrices()
    {
        Assert.Null(TokenPriceMapping.Map(1_000_000, null, null, null, null, pricesInNanoAiu: false));
    }
}
