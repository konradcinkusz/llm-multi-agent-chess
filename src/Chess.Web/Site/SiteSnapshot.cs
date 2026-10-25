using Chess.Web.Api;

namespace Chess.Web.Site;

/// <summary>
/// Everything the export reads from the API, loaded once so every page shows the same snapshot. Pure data: the loader
/// that fills it lives in <see cref="SiteExporter"/>.
/// </summary>
public sealed record SiteSnapshot(
    DateTimeOffset TakenAt,
    LeaderboardDto Board,                                                      // unscoped: all rated games
    MatrixDto Matrix,
    IReadOnlyList<ModelResultsDto> Breakdown,
    IReadOnlyDictionary<string, IReadOnlyList<RatingHistoryPointDto>> History,  // keyed by the models that get a chart colour
    IReadOnlyList<TournamentDto> Tournaments,
    IReadOnlyDictionary<Guid, LeaderboardDto> TournamentBoards,
    IReadOnlyList<GameSummaryDto> Games,                                       // finished, newest first
    IReadOnlyDictionary<Guid, GameUsageDto> Usage);                            // by game id, for the finished games

/// <summary>The snapshot as <c>data/snapshot.json</c>: when it was taken and what it counts.</summary>
public sealed record SiteSnapshotJson(DateTimeOffset TakenAt, int RatedGames, int Models, int Tournaments, int Games);
