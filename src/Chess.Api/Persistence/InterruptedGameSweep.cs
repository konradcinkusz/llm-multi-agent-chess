using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Persistence;

/// <summary>
/// Start-up sweep (03 §7): a game that was started but never finished was cut off by a restart.
/// It is closed as interrupted, which the rating excludes. So is a game that a broadcast slot or a viewer request refers to
/// and that never started: nothing plays it after a restart (the runner queues again only games nobody else owns), and the
/// slot or request is retried or credited with a new game.
/// </summary>
public static class InterruptedGameSweep
{
    public const string Termination = "interrupted";

    public static async Task<int> SweepAsync(ArenaDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var games = await db.Games
            .Where(g => g.FinishedAt == null
                && (g.StartedAt != null
                    || db.BroadcastSlots.Any(slot => slot.GameId == g.Id)
                    || db.GameRequests.Any(request => request.GameId == g.Id)))
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
