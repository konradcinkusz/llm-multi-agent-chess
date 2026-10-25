namespace Chess.Api.Tournaments;

/// <summary>
/// The response of <c>GET /api/cost/inputs</c>: what the forms need to show credits and USD before anything is saved
/// (03 §10, an addition authorised for G5 by the supervisor). Models lists each model with stored moves; a model with
/// none is absent and its estimate is assumed.
/// </summary>
public sealed record CostInputsDto(double UsdPerAiCredit, int AssumedInputTokensPerMove, int AssumedOutputTokensPerMove,
    IReadOnlyList<ModelTokenAverageDto> Models);

/// <summary>A model's average tokens per move over its stored moves that reported both counts.</summary>
public sealed record ModelTokenAverageDto(string ModelId, int Moves, double? AverageInputTokens, double? AverageOutputTokens);
