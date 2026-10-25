using Chess.Llm;
using Chess.Api.Contracts;

namespace Chess.Api.Usage;

/// <summary>Turns a model's token prices in AI credits into the dollar figures the API and the UI show (03 §10.2).</summary>
public static class ModelPricing
{
    /// <returns>
    /// The prices in US dollars per million tokens, or null when the model lists no prices (or a batch size that cannot
    /// be divided by). A price the model does not list stays null.
    /// </returns>
    public static ModelPricingDto? ToDto(LlmTokenPrices? prices, double usdPerAiCredit)
    {
        if (prices is null || prices.BatchSize <= 0)
        {
            return null;
        }

        return new ModelPricingDto(
            PerMillion(prices.InputCredits, prices.BatchSize, usdPerAiCredit),
            PerMillion(prices.OutputCredits, prices.BatchSize, usdPerAiCredit),
            PerMillion(prices.CachedInputCredits, prices.BatchSize, usdPerAiCredit));
    }

    private static double? PerMillion(double? creditsPerBatch, long batchSize, double usdPerAiCredit) =>
        creditsPerBatch is { } credits ? credits * 1_000_000 / batchSize * usdPerAiCredit : null;
}
