using Microsoft.Extensions.Options;

namespace Chess.Web.Site;

/// <summary>
/// <c>Arena:Site:ExportOnceTo</c>: exports the site into that (empty) folder as soon as the app has started, then stops the
/// app. The exit code is 1 when the export failed. This is how the CI job builds the public site from the results archive
/// (docs/RESULTS-ARCHIVE.md) without a browser or an admin click. Unset, the service does nothing.
/// </summary>
public sealed class SiteExportOnceService(
    IConfiguration configuration,
    ISiteExporter exporter,
    IHostApplicationLifetime lifetime,
    ILogger<SiteExportOnceService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration["Arena:Site:ExportOnceTo"] is not { Length: > 0 } folder) return Task.CompletedTask;

        lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => RunAsync(folder)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync(string folder)
    {
        try
        {
            var result = await exporter.ExportAsync(folder, CancellationToken.None);
            logger.LogInformation("Site exported to {Folder}: {Pages} pages, {Games} games", folder, result.Pages, result.Games);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The site export to {Folder} failed", folder);
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
