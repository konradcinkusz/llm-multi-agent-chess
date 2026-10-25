using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// Auto-publish (03 §11): every Arena:Site:AutoPublishMinutes it publishes when a game has finished since the last publish.
/// A failed publish is not counted as a publish, so the next tick tries again. Off when the interval is 0.
/// </summary>
public sealed class SiteAutoPublisher(
    IOptions<SitePublishingOptions> options,
    SiteExportCoordinator coordinator,
    ISiteActivity activity,
    TimeProvider time,
    ILogger<SiteAutoPublisher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.AutoPublishMinutes;
        if (minutes <= 0)
        {
            return;
        }

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

    /// <summary>One auto-publish check. Null when nothing was due (off, or no game finished since the last publish).</summary>
    public async Task<SitePublishResult?> TickAsync(CancellationToken ct)
    {
        if (options.Value.AutoPublishMinutes <= 0)
        {
            return null;
        }

        var latest = await activity.LatestFinishedAsync(ct);
        if (latest is null || latest <= coordinator.PublishedThrough)
        {
            return null;
        }
        return await coordinator.PublishAsync(ct);
    }
}
