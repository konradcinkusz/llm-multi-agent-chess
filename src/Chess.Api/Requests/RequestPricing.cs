using Chess.Api.Contracts;
using Chess.Api.Tournaments;

namespace Chess.Api.Requests;

/// <summary>
/// Money on the ledger is whole millionths of a unit, so the balance never drifts. The DTOs carry doubles; the conversion is here
/// and nowhere else.
/// </summary>
public static class Money
{
    private const double Micro = 1_000_000;

    public static long FromUnits(double units) => (long)Math.Round(units * Micro, MidpointRounding.AwayFromZero);

    public static double ToUnits(long micros) => micros / Micro;
}

/// <summary>What one Super Chat is worth in USD, or why it is worth nothing.</summary>
/// <param name="UsdMicros">The gross amount in micro-dollars: 0 for an unknown currency or an invalid amount.</param>
/// <param name="Reason">Why the amount is 0, or null when it was converted.</param>
public sealed record DepositValue(long UsdMicros, string? Reason);

public static class RequestPricing
{
    /// <summary>The moves one side makes in a game (03 §15.2). Shared with the tournament estimate.</summary>
    public const double MovesPerSide = CostEstimator.MovesPerSidePerGame;

    /// <summary>Converts a Super Chat amount, in millionths of its currency, to USD. Validates the amount and the currency on the server.</summary>
    public static DepositValue Deposit(long? amountMicros, string? currency, IReadOnlyDictionary<string, double> usdPerUnit)
    {
        if (amountMicros is not { } micros || micros <= 0)
        {
            return new DepositValue(0, "invalid amount");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return new DepositValue(0, "currency missing");
        }

        if (!usdPerUnit.TryGetValue(currency.Trim().ToUpperInvariant(), out var rate))
        {
            return new DepositValue(0, "currency not configured");
        }

        return new DepositValue(Math.Max(0, (long)Math.Round(micros * rate, MidpointRounding.AwayFromZero)), null);
    }

    /// <summary>
    /// The expected USD cost of one side of one game. The tokens a move takes come from the player's own history, then its
    /// model's, then the prompt style's assumption. Null when the model lists no prices, so it cannot be quoted.
    /// </summary>
    public static double? SideEstimateUsd(string model, ModelPricingDto? pricing, ModelTokenAverage? history, StyleTokens assumption, double usdPerAiCredit)
    {
        if (pricing is null)
        {
            return null;
        }

        // CostEstimator takes its tokens from a history. An assumption is passed as a one-move history, so the price is the
        // same formula the tournaments use; only the token source differs.
        var tokens = history is { Moves: > 0 } ? history : new ModelTokenAverage(1, assumption.InputTokens, assumption.OutputTokens);
        return CostEstimator.Estimate(model, MovesPerSide, new ModelCostInputs(null, pricing, tokens), usdPerAiCredit).Usd;
    }

    /// <summary>
    /// The price of a matchup: <c>needed = (white + black) × SafetyFactor / (1 − FeeShare)</c>, and the smallest tier that covers
    /// it. Null when either side is unpriced or when no tier covers it, so the matchup is not offered (03 §15.2).
    /// </summary>
    public static double? PriceUsd(double? whiteUsd, double? blackUsd, RequestOptions options)
    {
        if (whiteUsd is not { } white || blackUsd is not { } black)
        {
            return null;
        }

        var needed = (white + black) * options.SafetyFactor / (1 - options.FeeShare);
        return options.Tiers.Where(tier => tier >= needed).Select(tier => (double?)tier).Min();
    }
}
