using Chess.Api.Catalog;
using Chess.Api.Contracts;

namespace Chess.Api.Tournaments;

/// <summary>What the estimate knows about one model: its premium multiplier and listed token prices, and its stored moves.</summary>
public sealed record ModelCostInputs(double? PremiumMultiplier, ModelPricingDto? Pricing, ModelTokenAverage? History);

/// <summary>
/// One model's expected cost. <see cref="Basis"/> is <c>free</c>, <c>history</c>, <c>assumed</c> or <c>unknown</c>;
/// <see cref="SampleMoves"/> is the number of stored moves the tokens were averaged over (history only).
/// </summary>
public sealed record ModelCostEstimate(double? AiCredits, double? Usd, string Basis, int? SampleMoves);

/// <summary>
/// The expected cost of a model's moves (03 §10.2, ADR-012): expected moves × (input and output tokens per move) × the
/// model's token prices, converted to AI credits and then to USD. The tokens are the model's average over its stored
/// moves when it has any, else the assumed tokens below. Cached input is priced as uncached input (an upper bound).
/// Pure: no I/O, no clock.
/// </summary>
public static class CostEstimator
{
    public const string FreeBasis = "free";
    public const string HistoryBasis = "history";
    public const string AssumedBasis = "assumed";
    public const string UnknownBasis = "unknown";

    /// <summary>The moves one side makes in an 80-ply game (03 §9.1: plies ÷ 2).</summary>
    public const double MovesPerSidePerGame = 40;

    /// <summary>Input tokens a move is assumed to take when a model has no stored moves (03 §10.2). Not configurable.</summary>
    public const double AssumedInputTokensPerMove = 700;

    /// <summary>Output tokens a move is assumed to take when a model has no stored moves (03 §10.2). Not configurable.</summary>
    public const double AssumedOutputTokensPerMove = 300;

    /// <param name="model">The model id. The random player costs nothing.</param>
    /// <param name="expectedMoves">The moves the model is expected to make.</param>
    /// <param name="inputs">The model's catalog entry and stored moves; null when the model is not listed.</param>
    /// <param name="usdPerAiCredit">US dollars per AI credit (<c>Arena:Cost:UsdPerAiCredit</c>).</param>
    public static ModelCostEstimate Estimate(string model, double expectedMoves, ModelCostInputs? inputs, double usdPerAiCredit)
    {
        if (model == ModelCatalog.RandomModelId)
        {
            return new ModelCostEstimate(0, 0, FreeBasis, null);
        }

        // Both prices are needed: an estimate with one side missing would understate the cost.
        if (inputs?.Pricing is not { InputUsdPerMillionTokens: { } inputUsd, OutputUsdPerMillionTokens: { } outputUsd })
        {
            return new ModelCostEstimate(null, null, UnknownBasis, null);
        }

        var history = inputs.History;
        var (inputTokens, outputTokens, basis, sample) = history is { Moves: > 0 }
            ? (history.AverageInputTokens, history.AverageOutputTokens, HistoryBasis, (int?)history.Moves)
            : (AssumedInputTokensPerMove, AssumedOutputTokensPerMove, AssumedBasis, null);

        var usd = expectedMoves * (inputTokens * inputUsd + outputTokens * outputUsd) / 1_000_000;
        return new ModelCostEstimate(usd / usdPerAiCredit, usd, basis, sample);
    }
}
