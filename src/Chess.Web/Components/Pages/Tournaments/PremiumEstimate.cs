using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>
/// The premium-request estimate of a tournament (ADR-004), computed in the form before the user confirms. It is the
/// API's formula (<c>TournamentService.EstimateAsync</c>), so the figure shown before and after creation agree.
/// </summary>
public static class PremiumEstimate
{
    /// <summary>Premium requests a model is expected to make in one game: its moves on one side (40, 03 §9.1; ADR-004).</summary>
    public const int RequestsPerGame = 40;

    /// <summary>One model's estimate. <see cref="Requests"/> is null when its premium multiplier is not known.</summary>
    public sealed record ModelEstimate(string ModelId, double? Requests);

    /// <param name="catalog">The models from <c>GET /api/models</c>, with their premium multipliers.</param>
    /// <param name="models">The models selected for the tournament.</param>
    /// <param name="gamesPerPair">The games each pair of models plays.</param>
    public static IReadOnlyList<ModelEstimate> For(IReadOnlyList<ModelDto> catalog, IReadOnlyList<string> models, int gamesPerPair)
    {
        // Each model plays every other selected model gamesPerPair times.
        var gamesPerModel = (models.Count - 1) * gamesPerPair;
        return models
            .Select(id => new ModelEstimate(id, Multiplier(catalog, id) is { } value ? gamesPerModel * RequestsPerGame * value : null))
            .ToArray();
    }

    /// <summary>The sum of the known estimates. Unknown ones are not counted; the page says how many there are.</summary>
    public static double Known(IEnumerable<ModelEstimate> estimates) => estimates.Sum(estimate => estimate.Requests ?? 0);

    // A styled player is billed as its model (03 §14.4), so the multiplier is the model's.
    private static double? Multiplier(IReadOnlyList<ModelDto> catalog, string playerId) =>
        catalog.FirstOrDefault(model => model.Id == PromptStyleChoices.ModelOf(playerId))?.PremiumMultiplier;
}
