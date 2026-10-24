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
/// The premium requests one model is expected to use in a tournament. <c>null</c> when the model's premium multiplier is
/// not known (the gateway is not ready, or the model is not in its list).
/// </summary>
public sealed record ModelPremiumEstimateDto(string ModelId, double? EstimatedPremiumRequests);
