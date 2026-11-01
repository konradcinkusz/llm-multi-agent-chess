using Chess.Api.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Results.Archive;

/// <summary>Where the start-up import stands: <c>off</c> (no folder), <c>importing</c>, <c>done</c>. A failed import stops the app.</summary>
public sealed class ArchiveImportStatus
{
    private volatile string _state = "off";
    private int _added;

    public string State => _state;

    public int Added => _added;

    public void Importing() => _state = "importing";

    public void Done(int added)
    {
        _added = added;
        _state = "done";
    }
}

/// <summary>
/// Adds the games of <c>Arena:Results:Archive:ImportFolder</c> to the database once it is ready. A folder with an invalid file
/// stops the app (the host stops on a failed background service), so the CI job that builds the site fails instead of
/// publishing a ranking that silently left a game out.
/// </summary>
public sealed class ArchiveImportService(
    IOptions<ArchiveOptions> options,
    DatabaseReadiness readiness,
    ResultsArchive archive,
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
        status.Done(result.Added);
        logger.LogInformation("Results archive imported: {Added} game(s) added, {There} already there", result.Added, result.AlreadyThere);
    }
}
