using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// The two YouTube Data API v3 calls the intake makes (03 §15.5): <c>liveBroadcasts.list</c> for the active broadcast's chat,
/// and <c>liveChatMessages.list</c> for a page of its chat. Plain <see cref="HttpClient"/>. The access token is passed in per
/// call, so the caller decides when it is refreshed.
/// </summary>
public sealed class YouTubeChatClient(HttpClient http, YouTubeIntakeOptions options)
{
    /// <summary>The live chat id of the channel's active broadcast, or null when no broadcast is live (an idle stream).</summary>
    public async Task<string?> FindActiveLiveChatIdAsync(string accessToken, CancellationToken ct)
    {
        var body = await GetAsync(Endpoint("liveBroadcasts", "part=snippet&mine=true&broadcastStatus=active"), accessToken, ct);
        var list = JsonSerializer.Deserialize<LiveBroadcastList>(body, JsonOptions.Wire);
        return list?.Items
            .Select(broadcast => broadcast.Snippet?.LiveChatId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));
    }

    /// <summary>One page of the chat, from the start when <paramref name="pageToken"/> is null.</summary>
    public async Task<LiveChatMessagesResponse> ListMessagesAsync(
        string accessToken, string liveChatId, string? pageToken, CancellationToken ct)
    {
        var query = $"liveChatId={Uri.EscapeDataString(liveChatId)}&part=snippet,authorDetails";
        if (pageToken is not null)
        {
            query += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        var body = await GetAsync(Endpoint("liveChat/messages", query), accessToken, ct);
        return JsonSerializer.Deserialize<LiveChatMessagesResponse>(body, JsonOptions.Wire) ?? new LiveChatMessagesResponse();
    }

    private string Endpoint(string path, string query)
    {
        var baseUrl = options.ApiBaseUrl.EndsWith('/') ? options.ApiBaseUrl : options.ApiBaseUrl + "/";
        return $"{baseUrl}{path}?{query}";
    }

    private async Task<string> GetAsync(string url, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new YouTubeApiException(response.StatusCode, ErrorReason(body));
    }

    /// <summary>The first reason of a Google error body (<c>error.errors[0].reason</c>), or its status. Null when the body has neither.</summary>
    private static string? ErrorReason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString();
            }

            return error.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String ? status.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// YouTube answered with an error status. <see cref="Reason"/> is the Google reason (for example <c>quotaExceeded</c> or
/// <c>liveChatEnded</c>), which the poller reads to choose the back-off. The response body is not kept.
/// </summary>
public sealed class YouTubeApiException(HttpStatusCode status, string? reason)
    : Exception($"YouTube answered {(int)status}{(reason is null ? string.Empty : $" ({reason})")}.")
{
    public HttpStatusCode Status { get; } = status;

    public string? Reason { get; } = reason;
}
