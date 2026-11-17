using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// Auto-publish (03 §11): every Arena:Site:AutoPublishMinutes it publishes when the number of finished games has changed since
/// the last publish. The count is read when the service starts, so a restart does not publish by itself. A failed publish is
/// not counted as a publish, so the next tick tries again. Off when the interval is 0.
/// </summary>
public sealed class SiteAutoPublisher(
    IOptions<SitePublishingOptions> options,
    SiteExportCoordinator coordinator,
    ISiteActivity activity,
    TimeProvider time,
    ILogger<SiteAutoPublisher> log) : BackgroundService
{
    /// <summary>The number of finished games the last publish was due for, or the count at start-up. Null until it has been read.</summary>
    private int? _published;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.AutoPublishMinutes;
        if (minutes <= 0)
        {
            return;
        }

        await ReadBaselineAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await TickAsync(stoppingToken);
                if (result is { Published: false })
                {
                    log.LogWarning("Auto-publish of the results site was not published: {Detail}", result.Detail);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Auto-publish of the results site failed; it is tried again at the next interval");
            }
        }
    }

    /// <summary>
    /// Reads the number of finished games the app starts with. When the API cannot answer yet, the first tick that can takes
    /// the count instead and publishes nothing.
    /// </summary>
    public async Task ReadBaselineAsync(CancellationToken ct)
    {
        try
        {
            _published = await activity.FinishedGamesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogInformation(ex, "Auto-publish could not read the number of finished games at start-up; the first tick that can will take it");
        }
    }

    /// <summary>One auto-publish check. Null when nothing was due (off, or no game finished since the last publish).</summary>
    public async Task<SitePublishResult?> TickAsync(CancellationToken ct)
    {
        if (options.Value.AutoPublishMinutes <= 0)
        {
            return null;
        }

        var finished = await activity.FinishedGamesAsync(ct);
        if (_published is not { } published)
        {
            _published = finished;
            return null;
        }
        if (finished == published)
        {
            return null;
        }

        var result = await coordinator.PublishAsync(ct);
        if (result.Published)
        {
            // The count is the one read before the export: a game that finished during it makes the next tick publish again.
            _published = finished;
        }
        return result;
    }
}
