using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Requests;

/// <summary>
/// Start-up recovery (03 §15.4): a request left <c>playing</c> by a restart is closed if its game finished, and otherwise goes
/// through the failure path: requeued once, then credited. It runs after the migration and the interrupted-game sweep, and
/// the queue hands nothing to the runner until it has finished (<see cref="RequestRecoveryGate"/>), so a request taken in this
/// process is never mistaken for one a restart left behind.
/// </summary>
public sealed class RequestRecovery(IDbContextFactory<ArenaDbContext> factory, GameRequestQueue queue, DatabaseReadiness readiness,
    RequestRecoveryGate gate, ILogger<RequestRecovery> logger) : BackgroundService
{
    public const string RestartReason = "interrupted by a restart";

    /// <summary>The PGN result of a game that ended with no outcome.</summary>
    private const string UnfinishedResult = "*";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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
        finally
        {
            // Open on every path: with no database nothing can be taken, and a failed recovery must not stall the queue for good.
            gate.Open();
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

            // A game that finished with a result was played. One that ended with no result (the sweep's "interrupted", or an
            // aborted game: no reply) was not, so the request goes through the failure path: requeued once, then credited.
            if (game?.FinishedAt is not null && game.Result != UnfinishedResult && game.Termination != InterruptedGameSweep.Termination)
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

/// <summary>
/// Opens once the start-up recovery is done. <see cref="GameRequestQueue.TakeNextAsync"/> waits for it, so the broadcast runner
/// (started beside the recovery) cannot take a request that the recovery would then see as left playing.
/// </summary>
public sealed class RequestRecoveryGate
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => ready.Task;

    public void Open() => ready.TrySetResult();
}
