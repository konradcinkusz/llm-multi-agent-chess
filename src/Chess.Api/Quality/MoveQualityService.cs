using Chess.Analysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Quality;

/// <summary>
/// Runs <see cref="MoveQualityPipeline"/> every <see cref="MoveQualityOptions.Interval"/>. When the analyzer is
/// unavailable the worker returns at once: no timer and no database reads.
/// </summary>
public sealed class MoveQualityService(
    MoveQualityPipeline pipeline,
    IMoveQualityAnalyzer analyzer,
    TimeProvider clock,
    IOptions<MoveQualityOptions> options,
    ILogger<MoveQualityService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!analyzer.IsAvailable)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var annotated = await pipeline.AnnotatePendingAsync(stoppingToken);
                if (annotated > 0)
                {
                    logger.LogInformation("Scored move quality for {Games} game(s).", annotated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Move-quality pass failed; retrying on the next tick.");
            }
        }
    }
}
