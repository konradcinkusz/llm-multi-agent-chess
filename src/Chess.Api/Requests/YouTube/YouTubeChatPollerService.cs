using Microsoft.Extensions.Hosting;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// Runs <see cref="YouTubeChatPoller"/> until the host stops, waiting as long as each step asks. Registered only in
/// <c>YouTube</c> mode by <see cref="YouTubeIntakeRegistration"/>, so Mock and Off never poll.
/// </summary>
public sealed class YouTubeChatPollerService(YouTubeChatPoller poller) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = await poller.RunOnceAsync(stoppingToken);
            await Task.Delay(wait, stoppingToken);
        }
    }
}
