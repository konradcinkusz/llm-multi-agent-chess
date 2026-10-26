using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>
/// The AI-credit and USD estimate (03 §10.2) as the forms run it before anything is saved, so the figures on screen match
/// what the API will store. A port of <c>Chess.Api.Tournaments.CostEstimator</c>: the Web project may not reference Chess.Api
/// (P3), so <c>CostPreviewParityTests</c> holds the two equal.
/// </summary>
public static class CostPreview
{
    public const string RandomModelId = "random";

    /// <summary>The moves one side makes in an 80-ply game (03 §9.1: plies ÷ 2).</summary>
    public const double MovesPerSidePerGame = 40;

    /// <summary>One model's expected cost. Basis: free | history | assumed | unknown, as the API names it.</summary>
    public sealed record Estimate(double? AiCredits, double? Usd, string Basis, int? SampleMoves);

    /// <param name="model">The model, with its token prices (null when the API lists none).</param>
    /// <param name="inputs">The rate, the assumed tokens and the stored-move averages from <c>GET /api/cost/inputs</c>.</param>
    /// <param name="expectedMoves">The moves the model is expected to make.</param>
    public static Estimate For(ModelDto model, CostInputsDto inputs, double expectedMoves)
    {
        if (model.Id == RandomModelId)
        {
            return new Estimate(0, 0, "free", null);
        }

        // Both prices are needed: an estimate with one side missing would understate the cost.
        if (model.Pricing is not { InputUsdPerMillionTokens: { } inputUsd, OutputUsdPerMillionTokens: { } outputUsd })
        {
            return new Estimate(null, null, "unknown", null);
        }

        // A styled player (03 §14.4) with no history of its own is priced from its model's history.
        var history = UsableHistory(inputs, model.Id);
        if (history is null && PlayerId.Parse(model.Id) is { Style: not PlayerId.DefaultStyle } player)
        {
            history = UsableHistory(inputs, player.Model);
        }

        var fromHistory = history is not null;
        var inputTokens = fromHistory ? history!.AverageInputTokens!.Value : inputs.AssumedInputTokensPerMove;
        var outputTokens = fromHistory ? history!.AverageOutputTokens!.Value : inputs.AssumedOutputTokensPerMove;

        var usd = expectedMoves * (inputTokens * inputUsd + outputTokens * outputUsd) / 1_000_000;
        return new Estimate(usd / inputs.UsdPerAiCredit, usd, fromHistory ? "history" : "assumed",
            fromHistory ? history!.Moves : null);
    }

    /// <summary>The stored averages of one player or model, when there are moves to average.</summary>
    private static ModelTokenAverageDto? UsableHistory(CostInputsDto inputs, string playerId) =>
        inputs.Models.FirstOrDefault(entry => entry.ModelId == playerId) is { Moves: > 0, AverageInputTokens: not null, AverageOutputTokens: not null } history
            ? history
            : null;
}
