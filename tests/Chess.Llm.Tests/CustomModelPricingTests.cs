using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class CustomModelPricingTests
{
    private const string Secret = "sk-test-secret-value-do-not-print";

    [Fact]
    public void ACreditPairIsTheTokenPricesPerMillionTokens()
    {
        var prices = CustomModelPricing.TokenPrices(Model(c => (c.CreditsPerMillionInput, c.CreditsPerMillionOutput) = (300, 1500)), 0.01);

        Assert.Equal(new LlmTokenPrices(1_000_000, 300, 1500, null), prices);
    }

    [Fact]
    public void AUsdPairIsConvertedToCreditsWithTheDollarValueOfACredit()
    {
        // $3 and $15 a million tokens, at $0.01 an AI credit: 300 and 1 500 credits.
        var prices = CustomModelPricing.TokenPrices(Model(c => (c.UsdPerMillionInput, c.UsdPerMillionOutput) = (3.0, 15.0)), 0.01);

        Assert.NotNull(prices);
        Assert.Equal(1_000_000, prices.BatchSize);
        Assert.Equal(300.0, prices.InputCredits!.Value, precision: 6);
        Assert.Equal(1500.0, prices.OutputCredits!.Value, precision: 6);
        Assert.Null(prices.CachedInputCredits);
    }

    [Fact]
    public void CreditsWinWhenBothUnitsAreGiven()
    {
        var prices = CustomModelPricing.TokenPrices(
            Model(c => (c.UsdPerMillionInput, c.UsdPerMillionOutput, c.CreditsPerMillionInput, c.CreditsPerMillionOutput) = (3.0, 15.0, 7, 9)), 0.01);

        Assert.Equal(new LlmTokenPrices(1_000_000, 7, 9, null), prices);
    }

    [Fact]
    public void AModelWithNoPriceHasNoTokenPrices()
    {
        Assert.Null(CustomModelPricing.TokenPrices(Model(), 0.01));
    }

    [Fact]
    public void AnIncompletePairIsNoPrice()
    {
        Assert.Null(CustomModelPricing.TokenPrices(Model(c => c.UsdPerMillionInput = 3.0), 0.01));
        Assert.Null(CustomModelPricing.TokenPrices(Model(c => c.CreditsPerMillionOutput = 1500), 0.01));
    }

    [Fact]
    public void AModelWithoutPricesFailsTheMenuCheckWithAMessageNamingIt()
    {
        var message = CustomModelPricing.MissingPricesMessage(Model(c => c.Id = "tiny"));

        Assert.NotNull(message);
        Assert.StartsWith("Custom model tiny has no prices", message, StringComparison.Ordinal);
        Assert.Contains("UsdPerMillionInput", message, StringComparison.Ordinal);
        Assert.Contains("CreditsPerMillionOutput", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelWithAPairPassesTheMenuCheck()
    {
        Assert.Null(CustomModelPricing.MissingPricesMessage(Model(c => (c.UsdPerMillionInput, c.UsdPerMillionOutput) = (3.0, 15.0))));
        Assert.Null(CustomModelPricing.MissingPricesMessage(Model(c => (c.CreditsPerMillionInput, c.CreditsPerMillionOutput) = (300, 1500))));
    }

    [Fact]
    public void NeitherThePriceMessageNorTheTokenPricesCarryTheApiKey()
    {
        var model = Model(c =>
        {
            c.ApiKey = Secret;
            c.Id = "claude";
        });

        Assert.DoesNotContain(Secret, CustomModelPricing.MissingPricesMessage(model) ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, model.ToString(), StringComparison.Ordinal);
    }

    private static CustomModelOptions Model(Action<CustomModelOptions>? change = null)
    {
        var model = new CustomModelOptions
        {
            Id = "tiny",
            ProviderType = "anthropic",
            BaseUrl = "https://api.anthropic.com",
            Model = "claude-sonnet-5-5",
        };
        change?.Invoke(model);
        return model;
    }
}
