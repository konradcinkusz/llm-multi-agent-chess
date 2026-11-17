using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Api;

public class SseReaderTests
{
    [Fact]
    public async Task ReadsTheEventNameAndTheJsonData()
    {
        var messages = await ReadAll("event: MovePlayed\ndata: {\"ply\":1}\n\n");

        var message = Assert.Single(messages);
        Assert.Equal("MovePlayed", message.Type);
        Assert.Equal(1, message.Data.GetProperty("ply").GetInt32());
    }

    [Fact]
    public async Task JoinsMultiLineDataWithNewlinesAndDropsOneLeadingSpace()
    {
        // The JSON is split over three data lines; only the first space after "data:" is removed.
        var messages = await ReadAll("event: MovePlayed\ndata: {\ndata:  \"ply\": 2\ndata: }\n\n");

        var message = Assert.Single(messages);
        Assert.Equal(2, message.Data.GetProperty("ply").GetInt32());
    }

    [Fact]
    public async Task IgnoresCommentsAndTheHeartbeat()
    {
        var messages = await ReadAll(": ping\n\n: a comment\nevent: GameFinished\ndata: {\"result\":\"1-0\"}\n\n: ping\n\n");

        var message = Assert.Single(messages);
        Assert.Equal("GameFinished", message.Type);
        Assert.Equal("1-0", message.Data.GetProperty("result").GetString());
    }

    [Fact]
    public async Task AnEventWithoutANameIsAMessage()
    {
        var messages = await ReadAll("data: {}\n\n");

        Assert.Equal("message", Assert.Single(messages).Type);
    }

    [Fact]
    public async Task AnEventWithoutDataIsNotDispatched()
    {
        var messages = await ReadAll("event: MovePlayed\n\n");

        Assert.Empty(messages);
    }

    [Fact]
    public async Task AStreamThatEndsMidEventDiscardsThatEvent()
    {
        var messages = await ReadAll("event: MovePlayed\ndata: {}\n");

        Assert.Empty(messages);
    }

    [Fact]
    public async Task AcceptsCrLfLineEndings()
    {
        var messages = await ReadAll("event: MovePlayed\r\ndata: {}\r\n\r\n");

        Assert.Equal("MovePlayed", Assert.Single(messages).Type);
    }

    [Fact]
    public async Task KeepsEventsInOrderAndResetsTheNameBetweenEvents()
    {
        var messages = await ReadAll("event: MoveStarted\ndata: {\"ply\":1}\n\ndata: {\"ply\":2}\n\nevent: GameFinished\ndata: {}\n\n");

        Assert.Equal(new[] { "MoveStarted", "message", "GameFinished" }, messages.Select(m => m.Type).ToArray());
    }

    [Fact]
    public async Task StreamAsyncReadsTheEventsEndpointAndAcceptsEventStream()
    {
        var gameId = Guid.NewGuid();
        var handler = new FakeHandler(_ => Canned.Sse("event: GameFinished\ndata: {\"result\":\"1/2-1/2\"}\n\n"));
        var client = new GameEventsClient(Canned.Client(handler));

        var messages = new List<GameEventMessage>();
        await foreach (var message in client.StreamAsync(gameId))
        {
            messages.Add(message);
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal($"/api/games/{gameId}/events", request.Target);
        Assert.Equal("text/event-stream", request.Accept);
        Assert.Equal("GameFinished", Assert.Single(messages).Type);
    }

    [Fact]
    public async Task AStreamThatGoesSilentEndsAfterTheIdleTimeoutSoThePageReconnects()
    {
        using var reader = new StallingReader(lines: ["event: MovePlayed", "data: {\"ply\":1}", ""]);
        var messages = new List<GameEventMessage>();

        await foreach (var message in SseReader.ReadAsync(reader, idleTimeout: TimeSpan.FromMilliseconds(150)))
        {
            messages.Add(message);
        }

        Assert.Equal("MovePlayed", Assert.Single(messages).Type);
    }

    [Fact]
    public async Task HeartbeatsCountAsActivityAndKeepTheStreamOpen()
    {
        // Pings every 60 ms for 600 ms, far past the 200 ms idle limit, then an event: the stream was never idle.
        var lines = Enumerable.Repeat(new[] { ": ping", "" }, 10).SelectMany(x => x)
            .Concat(["event: GameFinished", "data: {\"result\":\"1-0\"}", ""]).ToArray();
        using var reader = new StallingReader(lines, delayBetweenLines: TimeSpan.FromMilliseconds(30));
        var messages = new List<GameEventMessage>();

        await foreach (var message in SseReader.ReadAsync(reader, idleTimeout: TimeSpan.FromMilliseconds(200)))
        {
            messages.Add(message);
        }

        Assert.Equal("GameFinished", Assert.Single(messages).Type);
    }

    [Fact]
    public async Task CancellationByThePageStillThrows()
    {
        using var reader = new StallingReader(lines: []);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in SseReader.ReadAsync(reader, cts.Token, TimeSpan.FromSeconds(30)))
            {
            }
        });
    }

    [Fact]
    public void TheDefaultIdleTimeoutIsThreeHeartbeats()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), SseReader.DefaultIdleTimeout);
    }

    /// <summary>Hands out <paramref name="lines"/> and then waits for ever, like a socket that stopped answering.</summary>
    private sealed class StallingReader(string[] lines, TimeSpan? delayBetweenLines = null) : TextReader
    {
        private int _next;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            if (_next < lines.Length)
            {
                if (delayBetweenLines is { } delay)
                {
                    await Task.Delay(delay, ct);
                }

                return lines[_next++];
            }

            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
    }

    private static async Task<List<GameEventMessage>> ReadAll(string text)
    {
        var messages = new List<GameEventMessage>();
        await foreach (var message in SseReader.ReadAsync(new StringReader(text)))
        {
            messages.Add(message);
        }

        return messages;
    }
}
