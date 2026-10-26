using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Requests;

/// <summary>
/// Start-up recovery (03 §15.4): a request left <c>playing</c> by a restart is closed if its game finished, and otherwise goes
/// through the failure path: requeued once, then credited. It runs after the migration and the interrupted-game sweep.
/// </summary>
public sealed class RequestRecovery(IDbContextFactory<ArenaDbContext> factory, GameRequestQueue queue, DatabaseReadiness readiness,
    ILogger<RequestRecovery> logger) : BackgroundService
{
    public const string RestartReason = "interrupted by a restart";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readiness.Finished.WaitAsync(stoppingToken);
        if (readiness.Current.State != DatabaseState.Ready)
        {
            return;
        }

        var recovered = await RecoverAsync(factory, queue, stoppingToken);
        if (recovered > 0)
        {
            logger.LogInformation("Recovered {Count} game request(s) left playing by a restart", recovered);
        }
    }

    /// <returns>The number of requests that were playing when the process started.</returns>
    public static async Task<int> RecoverAsync(IDbContextFactory<ArenaDbContext> factory, GameRequestQueue queue, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var playing = await db.GameRequests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Playing)
            .OrderBy(r => r.StartedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);

        foreach (var request in playing)
        {
            var game = request.GameId is { } gameId
                ? await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct)
                : null;

            // A game the sweep closed as interrupted did not finish; any other finished game did, so the request is played.
            if (game?.FinishedAt is not null && game.Termination != InterruptedGameSweep.Termination)
            {
                await queue.PlayedAsync(request.Id, ct);
            }
            else
            {
                await queue.FailedAsync(request.Id, RestartReason, ct);
            }
        }

        return playing.Count;
    }
}
