using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Persistence;

/// <summary>
/// Start-up sweep (03 §7): a game that was started but never finished was cut off by a restart.
/// It is closed as interrupted, which the rating excludes.
/// </summary>
public static class InterruptedGameSweep
{
    public const string Termination = "interrupted";

    public static async Task<int> SweepAsync(ArenaDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var games = await db.Games
            .Where(g => g.StartedAt != null && g.FinishedAt == null)
            .ToListAsync(ct);

        foreach (var game in games)
        {
            game.Result = "*";
            game.Termination = Termination;
            game.FinishedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return games.Count;
    }
}
