namespace Chess.Llm;

/// <summary>
/// The prices of a custom model (03 §12.1, §15.2), in the unit the catalog and the cost figures use: AI credits per million
/// tokens. A model is priced by a complete pair, input and output, in credits or in US dollars; credits win when both are
/// given. Prices are never guessed: a model with no complete pair has none.
/// </summary>
public static class CustomModelPricing
{
    private const long MillionTokens = 1_000_000;

    /// <param name="model">The custom model.</param>
    /// <param name="usdPerAiCredit">The dollar value of one AI credit (<c>Arena:Cost:UsdPerAiCredit</c>).</param>
    /// <returns>The prices in AI credits per million tokens, or null when the model has no complete pair.</returns>
    public static LlmTokenPrices? TokenPrices(CustomModelOptions model, double usdPerAiCredit)
    {
        if (model.CreditsPerMillionInput is { } creditsIn && model.CreditsPerMillionOutput is { } creditsOut)
        {
            return new LlmTokenPrices(MillionTokens, creditsIn, creditsOut, null);
        }

        if (model.UsdPerMillionInput is { } usdIn && model.UsdPerMillionOutput is { } usdOut)
        {
            return new LlmTokenPrices(MillionTokens, usdIn / usdPerAiCredit, usdOut / usdPerAiCredit, null);
        }

        return null;
    }

    /// <summary>
    /// Why a model cannot be quoted, or null when it has prices. A model on the request menu must pass this (03 §15.2).
    /// The message names the entry and never quotes a key.
    /// </summary>
    public static string? MissingPricesMessage(CustomModelOptions model)
    {
        var priced = (model.CreditsPerMillionInput is not null && model.CreditsPerMillionOutput is not null)
            || (model.UsdPerMillionInput is not null && model.UsdPerMillionOutput is not null);

        return priced
            ? null
            : $"Custom model {model.Id} has no prices: set UsdPerMillionInput and UsdPerMillionOutput, or CreditsPerMillionInput and CreditsPerMillionOutput.";
    }
}
