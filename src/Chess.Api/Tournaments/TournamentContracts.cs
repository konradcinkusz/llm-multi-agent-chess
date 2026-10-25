using Chess.Api.Contracts;

namespace Chess.Api.Tournaments;

/// <summary>The values of <c>TournamentDto.Status</c> (03 §5.1).</summary>
public static class TournamentStatus
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// The response to <c>POST /api/tournaments</c>: the new tournament, and the premium requests it is expected to cost
/// (ADR-004). <see cref="TournamentDto"/> stays as frozen in 03 §5.1; the estimate is only shown when a tournament is created.
/// </summary>
public sealed record TournamentCreatedDto(TournamentDto Tournament, IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);

/// <summary>
/// What one model is expected to cost in a tournament or a broadcast day. <see cref="EstimatedPremiumRequests"/> is set only
/// for a model that still reports a premium multiplier; <c>null</c> when that multiplier is not known. The AI-credit and USD
/// estimates (03 §10.2) are set when the model's token prices are known. <see cref="TokenBasis"/> says where the tokens per
/// move came from: <c>history</c> (averaged over <see cref="SampleMoves"/> stored moves), <c>assumed</c>, <c>free</c> (the
/// random player) or <c>unknown</c> (no prices).
/// </summary>
public sealed record ModelPremiumEstimateDto(string ModelId, double? EstimatedPremiumRequests,
    double? EstimatedAiCredits = null, double? EstimatedUsd = null, string? TokenBasis = null, int? SampleMoves = null);
