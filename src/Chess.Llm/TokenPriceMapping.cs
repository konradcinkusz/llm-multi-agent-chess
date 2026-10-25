namespace Chess.Llm;

/// <summary>
/// Maps a model's published token prices to <see cref="LlmTokenPrices"/> in AI credits (03 §10.1). The runtime quotes
/// prices in the unit <c>models.listTokenPriceUnits</c> reports for the model: AI credits already, or nano-AIU, which
/// is divided by <see cref="UsageTally.NanoAiuPerAiCredit"/>. A price whose unit is not known is not guessed at.
/// </summary>
internal static class TokenPriceMapping
{
    /// <param name="batchSize">Tokens per billing batch, as the runtime lists it.</param>
    /// <param name="inputPrice">Cost of one batch of input tokens, in the unit below.</param>
    /// <param name="outputPrice">Cost of one batch of output tokens, in the unit below.</param>
    /// <param name="cacheReadPrice">Cost of one batch of cached input tokens; <paramref name="cachePrice"/> is the deprecated alias.</param>
    /// <param name="cachePrice">Deprecated alias of <paramref name="cacheReadPrice"/>, used only when that one is absent.</param>
    /// <param name="pricesInNanoAiu">
    /// The unit of the prices: true for nano-AIU, false for AI credits, null when <c>models.listTokenPriceUnits</c>
    /// reported none for the model.
    /// </param>
    /// <returns>The prices in AI credits, or null when there is no batch size, no unit, or no price at all.</returns>
    public static LlmTokenPrices? Map(long? batchSize, double? inputPrice, double? outputPrice,
        double? cacheReadPrice, double? cachePrice, bool? pricesInNanoAiu)
    {
        if (batchSize is not > 0 || pricesInNanoAiu is not { } nanoAiu)
        {
            return null;
        }

        var cached = cacheReadPrice ?? cachePrice;
        if (inputPrice is null && outputPrice is null && cached is null)
        {
            return null;
        }

        var scale = nanoAiu ? 1 / UsageTally.NanoAiuPerAiCredit : 1;
        return new LlmTokenPrices(batchSize.Value, Scale(inputPrice, scale), Scale(outputPrice, scale), Scale(cached, scale));
    }

    private static double? Scale(double? price, double scale) => price is { } p ? p * scale : null;
}
