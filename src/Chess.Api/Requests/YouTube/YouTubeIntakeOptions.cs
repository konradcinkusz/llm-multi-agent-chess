namespace Chess.Api.Requests.YouTube;

/// <summary>
/// The YouTube intake's settings (03 §15.5, ADR-016), read from <c>Arena:Requests:YouTube</c>. The client id, client secret
/// and refresh token are AppHost secret parameters, forwarded to the API as environment variables. They are never logged,
/// never stored, and never part of an error message.
/// </summary>
public sealed class YouTubeIntakeOptions
{
    public const string Section = "Arena:Requests:YouTube";

    /// <summary>The value of <c>Arena:Requests:Mode</c> that starts the intake.</summary>
    public const string Mode = "YouTube";

    /// <summary>The owner's OAuth client id (secret parameter <c>youtube-client-id</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The owner's OAuth client secret (secret parameter <c>youtube-client-secret</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The owner's refresh token for the channel (secret parameter <c>youtube-refresh-token</c>).</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>The Data API v3 base address. A trailing slash is added when it is missing.</summary>
    public string ApiBaseUrl { get; set; } = "https://www.googleapis.com/youtube/v3/";

    /// <summary>The OAuth token endpoint.</summary>
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// The shortest gap between two chat reads, in seconds (03 §15.5). A shorter <c>pollingIntervalMillis</c> from YouTube is
    /// raised to it, which keeps a 24-hour stream inside the default daily quota.
    /// </summary>
    public int MinPollSeconds { get; set; } = 45;

    /// <summary>The gap between two looks for an active broadcast while none is live. An inactive stream is idle, not an error.</summary>
    public int IdleSeconds { get; set; } = 60;

    /// <summary>The first back-off after an error; it doubles on each consecutive error up to <see cref="MaxBackoffSeconds"/>.</summary>
    public int ErrorBackoffSeconds { get; set; } = 15;

    /// <summary>The longest back-off after an error.</summary>
    public int MaxBackoffSeconds { get; set; } = 600;

    /// <summary>The back-off after the daily quota is exhausted (403 <c>quotaExceeded</c>). The quota does not come back sooner.</summary>
    public int QuotaBackoffSeconds { get; set; } = 3600;

    /// <summary>The names of the settings that are missing, for a startup error. Never the values.</summary>
    public IReadOnlyList<string> MissingSecrets()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            missing.Add($"{Section}:ClientId");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            missing.Add($"{Section}:ClientSecret");
        }

        if (string.IsNullOrWhiteSpace(RefreshToken))
        {
            missing.Add($"{Section}:RefreshToken");
        }

        return missing;
    }
}
