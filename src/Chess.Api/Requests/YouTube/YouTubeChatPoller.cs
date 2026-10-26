using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// Reads the owner's live chat and hands each mapped message to <see cref="IChatIntake"/> (03 §15.5, ADR-016). Each
/// <see cref="RunOnceAsync"/> makes one step and returns how long to wait before the next:
/// <list type="bullet">
/// <item>No active broadcast, or its chat has ended: idle for <see cref="YouTubeIntakeOptions.IdleSeconds"/>. Not an error.</item>
/// <item>A page read: the page's messages are recorded, then the next page token is kept. The wait is YouTube's
/// <c>pollingIntervalMillis</c>, never less than <see cref="YouTubeIntakeOptions.MinPollSeconds"/>.</item>
/// <item>A 401: the access token is refreshed once and the same request is retried.</item>
/// <item>Quota exhausted (403 <c>quotaExceeded</c>): a long back-off.</item>
/// <item>Any other error: an exponential back-off, and the same page is read again. A page is passed on only once all its
/// messages were recorded, so a failed intake is retried, and the intake is idempotent by message id.</item>
/// </list>
/// </summary>
public sealed class YouTubeChatPoller(
    YouTubeChatClient chat,
    GoogleTokenProvider tokens,
    IServiceScopeFactory scopes,
    YouTubeIntakeOptions options,
    TimeProvider clock,
    ILogger<YouTubeChatPoller> logger)
{
    /// <summary>Reasons that mean the broadcast's chat is not running any more (or not yet): the stream is idle.</summary>
    private static readonly HashSet<string> ChatNotRunningReasons = ["liveChatEnded", "liveChatDisabled", "liveChatNotFound"];

    /// <summary>Reasons that mean the daily quota is spent. The quota does not come back before its reset.</summary>
    private static readonly HashSet<string> QuotaReasons = ["quotaExceeded", "dailyLimitExceeded", "dailyLimitExceededUnreg"];

    private string? _liveChatId;
    private string? _pageToken;
    private int _errorStreak;

    /// <summary>Makes one step of the poll, and returns how long to wait before the next. Only cancellation escapes.</summary>
    public async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_liveChatId is null)
            {
                var found = await WithTokenAsync((token, c) => chat.FindActiveLiveChatIdAsync(token, c), ct);
                if (found is null)
                {
                    _errorStreak = 0;
                    return Idle();
                }

                _liveChatId = found;
                _pageToken = null;
                logger.LogInformation("Reading the live chat of the active broadcast");
            }

            var liveChatId = _liveChatId;
            var pageToken = _pageToken;
            var page = await WithTokenAsync((token, c) => chat.ListMessagesAsync(token, liveChatId, pageToken, c), ct);
            await RecordAsync(page, ct);

            _pageToken = page.NextPageToken ?? _pageToken;
            _errorStreak = 0;
            return PollInterval(page.PollingIntervalMillis);
        }
        catch (YouTubeApiException error) when (IsChatNotRunning(error))
        {
            logger.LogInformation("The live chat is not running ({Reason}); waiting for an active broadcast", error.Reason);
            _liveChatId = null;
            _pageToken = null;
            _errorStreak = 0;
            return Idle();
        }
        catch (YouTubeApiException error) when (error.Reason is { } reason && QuotaReasons.Contains(reason))
        {
            logger.LogWarning("The YouTube quota is spent ({Reason}); backing off for {Seconds}s", error.Reason, options.QuotaBackoffSeconds);
            return TimeSpan.FromSeconds(options.QuotaBackoffSeconds);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            _errorStreak++;
            var backoff = ErrorBackoff(_errorStreak);
            if (error is YouTubeApiException api)
            {
                logger.LogWarning("YouTube answered {Status} ({Reason}); retrying in {Seconds}s", (int)api.Status, api.Reason, backoff.TotalSeconds);
            }
            else
            {
                // The message of a failed token refresh names the error code only, never a secret.
                logger.LogWarning("The YouTube intake failed ({Error}); retrying in {Seconds}s", error.Message, backoff.TotalSeconds);
            }

            return backoff;
        }
    }

    /// <summary>Runs a call with the access token, and once more with a fresh token when YouTube answers 401.</summary>
    private async Task<T> WithTokenAsync<T>(Func<string, CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call(await tokens.GetAccessTokenAsync(ct), ct);
        }
        catch (YouTubeApiException error) when (error.Status == HttpStatusCode.Unauthorized)
        {
            tokens.Invalidate();
            return await call(await tokens.GetAccessTokenAsync(ct), ct);
        }
    }

    private async Task RecordAsync(LiveChatMessagesResponse page, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var intake = scope.ServiceProvider.GetRequiredService<IChatIntake>();
        var fallbackAt = clock.GetUtcNow();
        foreach (var message in page.Items)
        {
            if (YouTubeChatMapper.Map(message, fallbackAt, logger) is { } chatEvent)
            {
                await intake.AcceptAsync(chatEvent, ct);
            }
        }
    }

    private static bool IsChatNotRunning(YouTubeApiException error) =>
        error.Status == HttpStatusCode.NotFound || (error.Reason is { } reason && ChatNotRunningReasons.Contains(reason));

    private TimeSpan Idle() => TimeSpan.FromSeconds(options.IdleSeconds);

    /// <summary>YouTube's suggested gap, raised to the minimum; the minimum alone when YouTube suggests none.</summary>
    private TimeSpan PollInterval(long? pollingIntervalMillis)
    {
        var minimum = TimeSpan.FromSeconds(options.MinPollSeconds);
        return pollingIntervalMillis is { } millis ? TimeSpan.FromMilliseconds(Math.Max(millis, minimum.TotalMilliseconds)) : minimum;
    }

    private TimeSpan ErrorBackoff(int streak)
    {
        var seconds = options.ErrorBackoffSeconds * Math.Pow(2, Math.Min(streak - 1, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, options.MaxBackoffSeconds));
    }
}
