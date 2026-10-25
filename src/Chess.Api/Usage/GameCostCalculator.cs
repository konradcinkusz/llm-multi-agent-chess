using Chess.Api.Contracts;

namespace Chess.Api.Usage;

/// <summary>
/// The cost of a game's replies in AI credits and US dollars (03 §10.2, ADR-012). Pure: the caller loads the replies and
/// the model's prices; this adds them up. Cost and tokens never feed the rating (RATING.md §8).
/// </summary>
public static class GameCostCalculator
{
    public const string Free = "free";
    public const string Reported = "reported";
    public const string Estimated = "estimated";
    public const string Partial = "partial";
    public const string Unknown = "unknown";

    /// <summary>One reply's usage as stored. Every field is null when the runtime did not report it.</summary>
    public sealed record ReplyUsage(int? InputTokens, int? OutputTokens, int? ReasoningTokens, int? CachedInputTokens,
        double? ReportedCredits);

    /// <summary>One side of the game. The random side is free and has no replies, so it is passed with <paramref name="isLlm"/> false.</summary>
    /// <param name="prices">The model's prices in US dollars per million tokens; null when the model lists none.</param>
    public static SideUsageDto ForSide(string color, string modelId, bool isLlm, IReadOnlyList<ReplyUsage> replies,
        ModelPricingDto? prices, double usdPerAiCredit)
    {
        if (usdPerAiCredit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usdPerAiCredit), "The dollar value of an AI credit must be positive.");
        }

        if (!isLlm)
        {
            return new SideUsageDto(color, modelId, 0, 0, 0, 0, 0, 0, 0, Free);
        }

        var costs = replies.Select(r => CostOf(r, prices, usdPerAiCredit)).ToList();
        var known = costs.Where(c => c.Credits is not null).ToList();
        double? credits = known.Count == 0 ? null : known.Sum(c => c.Credits!.Value);

        return new SideUsageDto(color, modelId, replies.Count,
            replies.Sum(r => (long)(r.InputTokens ?? 0)),
            replies.Sum(r => (long)(r.OutputTokens ?? 0)),
            replies.Sum(r => (long)(r.ReasoningTokens ?? 0)),
            replies.Sum(r => (long)(r.CachedInputTokens ?? 0)),
            credits,
            credits is { } c ? c * usdPerAiCredit : null,
            BasisOf(costs.Count, known));
    }

    /// <summary>The whole game. A total is null only when neither side has a cost; otherwise it sums the sides that have one.</summary>
    public static GameUsageDto ForGame(Guid gameId, SideUsageDto white, SideUsageDto black, double usdPerAiCredit)
    {
        var credits = Sum(white.AiCredits, black.AiCredits);
        return new GameUsageDto(gameId, white, black, credits, credits is { } c ? c * usdPerAiCredit : null, usdPerAiCredit);
    }

    /// <returns>The credits of one reply, and whether they were reported; credits are null when they are unknown.</returns>
    private static (double? Credits, bool Reported) CostOf(ReplyUsage reply, ModelPricingDto? prices, double usdPerAiCredit)
    {
        if (reply.ReportedCredits is { } reported)
        {
            return (reported, true);
        }

        if (prices is null
            || reply.InputTokens is not { } input
            || reply.OutputTokens is not { } output
            || prices.InputUsdPerMillionTokens is not { } inputUsd
            || prices.OutputUsdPerMillionTokens is not { } outputUsd)
        {
            return (null, false);
        }

        // Cached input is billed at its own price when the model has one, else at the input price. Reasoning tokens are
        // already counted in the output tokens, so they add nothing here.
        var cached = Math.Min(reply.CachedInputTokens ?? 0, input);
        var cachedUsd = prices.CachedInputUsdPerMillionTokens ?? inputUsd;
        var usd = ((input - cached) * inputUsd + cached * cachedUsd + output * outputUsd) / 1_000_000d;
        return (usd / usdPerAiCredit, false);
    }

    private static string BasisOf(int replies, List<(double? Credits, bool Reported)> known)
    {
        if (known.Count == 0)
        {
            return Unknown;
        }

        if (known.Count < replies)
        {
            return Partial;
        }

        return known.All(c => c.Reported) ? Reported : Estimated;
    }

    private static double? Sum(double? a, double? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}
