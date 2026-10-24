using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results;

/// <summary>Reads <c>rating_snapshots</c> for the rating-over-time chart (03 §5).</summary>
public static class RatingHistory
{
    /// <param name="model">One model's history, or every model's when null.</param>
    /// <returns>Oldest first.</returns>
    public static Task<List<RatingHistoryPointDto>> ReadAsync(ArenaDbContext db, string? model, CancellationToken ct)
    {
        var snapshots = db.RatingSnapshots.AsNoTracking();
        if (model is not null)
        {
            snapshots = snapshots.Where(s => s.Model == model);
        }

        return snapshots
            .OrderBy(s => s.TakenAt).ThenBy(s => s.Id)
            .Select(s => new RatingHistoryPointDto(
                s.TakenAt, s.RatedGames, s.Model, s.Rating, s.CiLow, s.CiHigh, s.ArenaScore, s.Provisional))
            .ToListAsync(ct);
    }
}
