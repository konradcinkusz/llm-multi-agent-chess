namespace Chess.Api.Tournaments;

/// <summary>
/// Calls <see cref="TournamentService.DispatchAsync"/> on a timer. Games finish on their own, so the scheduler polls;
/// a failed tick is logged and retried on the next one.
/// </summary>
public sealed class TournamentScheduler(TournamentService tournaments, ILogger<TournamentScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await tournaments.DispatchAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Tournament scheduling failed; it is retried on the next tick");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; queued games stay as they are and the runner handles its own.
        }
    }
}
