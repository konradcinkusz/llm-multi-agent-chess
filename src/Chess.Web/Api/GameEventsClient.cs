using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Chess.Web.Api;

/// <summary>One SSE message: <c>event:</c> is the IGameEvent type name, <c>data:</c> its camelCase JSON (03 §5.1).</summary>
public sealed record GameEventMessage(string Type, JsonElement Data);

/// <summary>Reads <c>GET /api/games/{id}/events</c> as a line-by-line SSE stream. The stream replays nothing.</summary>
public sealed class GameEventsClient(HttpClient http)
{
    public async IAsyncEnumerable<GameEventMessage> StreamAsync(Guid gameId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/games/{gameId}/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await ApiFailure.EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        await foreach (var message in SseReader.ReadAsync(reader, ct))
        {
            yield return message;
        }
    }
}

/// <summary>Parses the subset of the SSE format the arena sends: <c>event</c>, multi-line <c>data</c>, comments (<c>: ping</c>).</summary>
public static class SseReader
{
    /// <summary>
    /// The arena sends a <c>: ping</c> comment every 15 s, so three missed beats mean the socket is stalled: the stream
    /// is then ended, like a closed one, and the page reconnects.
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Reads the events. Every line, a comment such as the heartbeat included, counts as activity; after
    /// <paramref name="idleTimeout"/> (default <see cref="DefaultIdleTimeout"/>) with none the enumeration ends.
    /// </summary>
    public static async IAsyncEnumerable<GameEventMessage> ReadAsync(TextReader reader,
        [EnumeratorCancellation] CancellationToken ct = default, TimeSpan? idleTimeout = null)
    {
        var idleLimit = idleTimeout ?? DefaultIdleTimeout;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string? eventType = null;
        var dataLines = new List<string>();

        while (true)
        {
            idle.CancelAfter(idleLimit);
            var (timedOut, line) = await NextLineAsync(reader, idle.Token, ct);
            if (timedOut || line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                // A blank line ends the event. An event without data is not dispatched.
                if (dataLines.Count > 0)
                {
                    yield return new GameEventMessage(eventType ?? "message", ParseData(dataLines));
                }

                eventType = null;
                dataLines.Clear();
                continue;
            }

            if (line[0] == ':')
            {
                continue; // comment, e.g. the ": ping" heartbeat
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    eventType = value;
                    break;
                case "data":
                    dataLines.Add(value);
                    break;
                // id and retry are not used by the arena.
            }
        }

        // A stream that ends mid-event discards that event, as the SSE format requires.
    }

    /// <summary>The next line, or <c>timedOut</c> when the idle token fired while <paramref name="ct"/> did not.</summary>
    private static async Task<(bool TimedOut, string? Line)> NextLineAsync(TextReader reader, CancellationToken idleToken,
        CancellationToken ct)
    {
        try
        {
            return (false, await reader.ReadLineAsync(idleToken));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (true, null);
        }
    }

    private static JsonElement ParseData(List<string> dataLines)
    {
        using var document = JsonDocument.Parse(string.Join('\n', dataLines));
        return document.RootElement.Clone();
    }
}
