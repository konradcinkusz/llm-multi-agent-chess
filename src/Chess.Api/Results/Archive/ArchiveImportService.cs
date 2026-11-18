using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Results.Archive;

/// <summary>
/// Where the start-up import stands: <c>off</c> (no folder), <c>importing</c>, <c>done</c>. <c>done</c> means the games are in the
/// database and their rating snapshots are written, so the rating history is complete. A failed import stops the app.
/// </summary>
public sealed class ArchiveImportStatus
{
    private volatile string _state = "off";
    private int _added;
    private int _snapshotBatches;

    public string State => _state;

    public int Added => _added;

    /// <summary>The rating snapshot batches the import wrote (one per rated game that had none).</summary>
    public int SnapshotBatches => _snapshotBatches;

    public void Importing() => _state = "importing";

    public void Done(int added, int snapshotBatches = 0)
    {
        _added = added;
        _snapshotBatches = snapshotBatches;
        _state = "done";
    }
}

/// <summary>
/// Adds the games of <c>Arena:Results:Archive:ImportFolder</c> to the database once it is ready, then writes their rating
/// snapshots before the status says <c>done</c>: the site export reads the rating history, which is empty until a snapshot
/// exists, and the snapshot service would only write them one interval later. A folder with an invalid file stops the app (the
/// host stops on a failed background service), so the CI job that builds the site fails instead of publishing a ranking that
/// silently left a game out.
/// </summary>
public sealed class ArchiveImportService(
    IOptions<ArchiveOptions> options,
    DatabaseReadiness readiness,
    ResultsArchive archive,
    IDbContextFactory<ArenaDbContext> factory,
    TimeProvider clock,
    ArchiveImportStatus status,
    ILogger<ArchiveImportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ImportFolder is not { Length: > 0 } folder) return;

        status.Importing();
        await readiness.Finished.WaitAsync(stoppingToken);
        if (readiness.Current.State != DatabaseState.Ready)
        {
            throw new InvalidOperationException("The database is not ready, so the results archive was not imported.");
        }

        var read = GameRecordFolder.Read(folder);
        if (read.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The results archive {folder} has {read.Errors.Count} problem(s):\n{string.Join('\n', read.Errors)}");
        }

        var result = await archive.ImportAsync(read.Records, stoppingToken);
        int batches;
        await using (var db = await factory.CreateDbContextAsync(stoppingToken))
        {
            batches = await RatingSnapshotWriter.WriteNewAsync(db, clock, stoppingToken);
        }

        status.Done(result.Added, batches);
        logger.LogInformation(
            "Results archive imported: {Added} game(s) added, {There} already there, {Batches} rating snapshot batch(es) written",
            result.Added, result.AlreadyThere, batches);
    }
}
