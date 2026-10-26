using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// Hands out the owner's OAuth access token, refreshed from the refresh token when it is missing or about to expire
/// (03 §15.5). Plain <see cref="HttpClient"/>, no Google package. The token is never logged.
/// </summary>
public sealed class GoogleTokenProvider(HttpClient http, YouTubeIntakeOptions options, TimeProvider clock)
{
    /// <summary>A token is refreshed this long before Google says it expires, so no request goes out with a token about to lapse.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _accessToken;
    private DateTimeOffset _expiresAt;

    /// <summary>A valid access token, refreshing it first when needed.</summary>
    /// <exception cref="InvalidOperationException">Google refused the refresh. The message names the error code, never a secret.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (Current() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (Current() is { } fresh)
            {
                return fresh;
            }

            var (accessToken, expiresInSeconds) = await RefreshAsync(ct);
            _expiresAt = clock.GetUtcNow() + TimeSpan.FromSeconds(expiresInSeconds) - Margin;
            _accessToken = accessToken;
            return accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the cached token, so the next call refreshes it. Used after YouTube answers 401.</summary>
    public void Invalidate() => _accessToken = null;

    private string? Current() => _accessToken is { } token && clock.GetUtcNow() < _expiresAt ? token : null;

    private async Task<(string AccessToken, long ExpiresInSeconds)> RefreshAsync(CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = options.RefreshToken,
            ["grant_type"] = "refresh_token",
        });

        using var response = await http.PostAsync(options.TokenUrl, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google refused the token refresh ({(int)response.StatusCode}, {ErrorCode(body) ?? "no error code"}). " +
                "Check the OAuth client and the refresh token in the AppHost secret parameters.");
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions.Wire);
        if (string.IsNullOrEmpty(token?.AccessToken))
        {
            throw new InvalidOperationException("Google's token refresh answered without an access token.");
        }

        return (token.AccessToken, token.ExpiresIn ?? 0);
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<TokenError>(body, JsonOptions.Wire)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Google's token endpoint answers in snake_case, unlike the YouTube Data API, so the names are spelled out.
    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long? ExpiresIn { get; set; }
    }

    private sealed class TokenError
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

/// <summary>The JSON options for the Google and YouTube wire formats: camelCase names, numbers read from strings as well.</summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
