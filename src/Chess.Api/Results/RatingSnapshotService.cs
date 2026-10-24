using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Results;

/// <summary>
/// Writes the due rating snapshots every <see cref="RatingSnapshotOptions.Interval"/> (03 §4, ADR-007).
/// The first pass runs one interval after start-up. A failed pass is logged and retried on the next tick;
/// the cursor only moves when a batch is saved, so nothing is skipped or written twice.
/// </summary>
public sealed class RatingSnapshotService(
    IDbContextFactory<ArenaDbContext> factory,
    TimeProvider clock,
    IOptions<RatingSnapshotOptions> options,
    ILogger<RatingSnapshotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await WriteDueSnapshotsAsync(stoppingToken);
        }
    }

    private async Task WriteDueSnapshotsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var batches = await RatingSnapshotWriter.WriteNewAsync(db, clock, ct);
            if (batches > 0)
            {
                logger.LogInformation("Wrote {Batches} rating snapshot batch(es).", batches);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Writing rating snapshots failed; retrying on the next tick.");
        }
    }
}
