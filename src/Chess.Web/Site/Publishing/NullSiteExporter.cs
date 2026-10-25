namespace Chess.Web.Site.Publishing;

/// <summary>
/// The exporter used until the real one is registered. It writes nothing and reports no pages, so a publish is refused
/// rather than replacing the public site with an empty one.
/// </summary>
public sealed class NullSiteExporter(TimeProvider time) : ISiteExporter
{
    public Task<SiteExportResult> ExportAsync(string outputDirectory, CancellationToken ct) =>
        Task.FromResult(new SiteExportResult(0, 0, 0, time.GetUtcNow()));
}
