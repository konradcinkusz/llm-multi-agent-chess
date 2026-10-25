namespace Chess.Api.Tournaments;

/// <summary>Bound from the <c>Arena:Cost</c> section (03 §10.2, ADR-012). The rate and the assumed tokens are unverified until the owner's live run.</summary>
public sealed class CostEstimateOptions
{
    /// <summary>US dollars per AI credit (1 credit = 0.01 USD on the Copilot price list).</summary>
    public double UsdPerAiCredit { get; set; } = 0.01;

    /// <summary>Input tokens a move is assumed to take when a model has no stored moves to average.</summary>
    public double AssumedInputTokensPerMove { get; set; } = 700;

    /// <summary>Output tokens a move is assumed to take when a model has no stored moves to average.</summary>
    public double AssumedOutputTokensPerMove { get; set; } = 300;
}
