namespace Chess.Api.Usage;

/// <summary>Bound from <c>Arena:Cost</c>. Copilot bills in AI credits; this is the dollar value of one credit (03 §10.2).</summary>
public sealed class CostOptions
{
    /// <summary>
    /// US dollars per AI credit. Copilot's published rate is 0.01; the UI always labels dollar figures as estimated.
    /// Must be positive.
    /// </summary>
    public double UsdPerAiCredit { get; set; } = 0.01;
}
