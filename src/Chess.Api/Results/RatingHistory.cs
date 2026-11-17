using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results;

/// <summary>Reads <c>rating_snapshots</c> for the rating-over-time chart (03 §5).</summary>
public static class RatingHistory
{
    /// <summary>
    /// The most points one model contributes. A model has one snapshot per rated game, so a long-running arena would send
    /// thousands of points per model for a chart a few hundred pixels wide. Above this the series is thinned at a fixed
    /// stride over <c>rated_games</c> (every model on the same x positions), and each model keeps its first and its latest
    /// snapshot, so the line starts and ends where the data does. Below it nothing is dropped.
    /// </summary>
    public const int MaxPointsPerModel = 200;

    /// <param name="model">One model's history, or every model's when null.</param>
    /// <returns>Oldest first.</returns>
    public static async Task<List<RatingHistoryPointDto>> ReadAsync(ArenaDbContext db, string? model, CancellationToken ct)
    {
        var snapshots = db.RatingSnapshots.AsNoTracking();
        if (model is not null)
        {
            snapshots = snapshots.Where(s => s.Model == model);
        }

        // Group by the model name, then aggregate: the shape PostgreSQL translates.
        var perModel = await Spans(snapshots).ToListAsync(ct);
        var longest = perModel.Count == 0 ? 0 : perModel.Max(m => m.Count);
        var stride = (longest + MaxPointsPerModel - 1) / MaxPointsPerModel;

        if (stride > 1)
        {
            var ends = perModel.SelectMany(m => new[] { m.First, m.Last }).ToList();
            snapshots = Thinned(snapshots, stride, ends);
        }

        return await Points(snapshots).ToListAsync(ct);
    }

    // Each query is its own method so a test can translate it for PostgreSQL (ToQueryString) without a server.
    internal static IQueryable<ModelSpan> Spans(IQueryable<RatingSnapshotEntity> snapshots) => snapshots
        .GroupBy(s => s.Model)
        .Select(group => new ModelSpan(group.Count(), group.Min(s => s.Id), group.Max(s => s.Id)));

    internal static IQueryable<RatingSnapshotEntity> Thinned(IQueryable<RatingSnapshotEntity> snapshots, int stride, List<long> ends) =>
        snapshots.Where(s => s.RatedGames % stride == 0 || ends.Contains(s.Id));

    // Order before projecting (lesson #78).
    internal static IQueryable<RatingHistoryPointDto> Points(IQueryable<RatingSnapshotEntity> snapshots) => snapshots
        .OrderBy(s => s.TakenAt).ThenBy(s => s.Id)
        .Select(s => new RatingHistoryPointDto(
            s.TakenAt, s.RatedGames, s.Model, s.Rating, s.CiLow, s.CiHigh, s.ArenaScore, s.Provisional));

    /// <summary>The number of snapshots of one model and the ids of its first and latest.</summary>
    internal sealed record ModelSpan(int Count, long First, long Last);
}
