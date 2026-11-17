using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// Runs one export or publish at a time: the admin page and auto-publish share the export folder. It keeps the last results
/// for the page, and PublishedThrough, the export time of the last successful publish, which auto-publish compares with the
/// newest finished game. It starts at the app's start, so a restart does not publish by itself.
/// </summary>
public sealed class SiteExportCoordinator(
    ISitePublisher publisher,
    IOptions<SitePublishingOptions> options,
    IHostEnvironment environment,
    TimeProvider time,
    ISiteExporter? exporter = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Without a registered exporter the coordinator reports "no pages" rather than publishing an empty site.
    private readonly ISiteExporter _exporter = exporter ?? new NullSiteExporter(time);

    public SiteExportResult? LastExport { get; private set; }

    public SitePublishResult? LastPublish { get; private set; }

    public DateTimeOffset PublishedThrough { get; private set; } = time.GetUtcNow();

    public bool Busy => _gate.CurrentCount == 0;

    public string ExportDirectory => options.Value.ResolvedExportDirectory;

    public Task<SiteLocation> LocateAsync(CancellationToken ct) =>
        GitRemote.LocateAsync(options.Value, environment.ContentRootPath, ct);

    public async Task<SiteExportResult> ExportAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return LastExport = await ExportCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Exports afresh and publishes the export. A failed export or an empty one is reported, never published.</summary>
    public async Task<SitePublishResult> PublishAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            SiteExportResult export;
            try
            {
                export = LastExport = await ExportCoreAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return LastPublish = new SitePublishResult(false, null, $"The export failed: {ex.Message}", time.GetUtcNow());
            }

            if (export.Pages == 0)
            {
                return LastPublish = new SitePublishResult(false, null, "The export wrote no pages, so nothing was published.", time.GetUtcNow());
            }

            var result = await publisher.PublishAsync(ExportDirectory, ct);
            if (result.Published)
            {
                PublishedThrough = export.TakenAt;
            }
            return LastPublish = result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Empties the export folder and exports into it. The folder is deleted only when it is one this export made (it holds
    /// the marker) or is empty, and never when it is the repository the app runs from, inside it or above it, or nested with
    /// the publisher's work folder: a mistyped <c>Arena:Site:ExportDirectory</c> must not delete anything the app did not write.
    /// </summary>
    private async Task<SiteExportResult> ExportCoreAsync(CancellationToken ct)
    {
        var folder = ExportDirectory;
        var roots = await SiteFolders.AppRootsAsync(options.Value.GitExecutable, environment.ContentRootPath, ct);
        if (SiteFolders.ExportProblem(folder, options.Value.ResolvedWorkDir, roots) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        if (Directory.Exists(folder))
        {
            if (File.Exists(Path.Combine(folder, SiteFolders.ExportMarker)))
            {
                Directory.Delete(folder, recursive: true);
            }
            else if (Directory.EnumerateFileSystemEntries(folder).Any())
            {
                throw new InvalidOperationException(
                    $"Arena:Site:ExportDirectory ({folder}) has content that the site export did not write, so it was not emptied. Choose an empty folder or remove it yourself.");
            }
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, SiteFolders.ExportMarker), "site export folder\n", ct);
        return await _exporter.ExportAsync(folder, ct);
    }
}
