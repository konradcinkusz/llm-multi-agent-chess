using Chess.Api.Games;
using Xunit;

namespace Chess.Api.Tests.Games;

public class InMemoryGameEventStreamTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubscribersReceiveOnlyTheEventsOfTheirGame()
    {
        var stream = new InMemoryGameEventStream();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscription = stream.SubscribeAsync(mine, cts.Token);

        stream.Publish(new MoveStarted(other, At, 1, "white", "random"));
        stream.Publish(new MoveStarted(mine, At, 1, "white", "random"));
        stream.Publish(new GameFinished(mine, At, "1/2-1/2", "adjudicated"));

        var received = new List<IGameEvent>();
        await foreach (var e in subscription)
        {
            received.Add(e);
        }

        var started = Assert.IsType<MoveStarted>(Assert.Single(received, e => e is MoveStarted));
        Assert.Equal(mine, started.GameId);
        Assert.Equal(2, received.Count);
        Assert.All(received, e => Assert.Equal(mine, e.GameId));
    }

    [Fact]
    public async Task EveryFanOutSubscriberSeesTheSameEvents()
    {
        var stream = new InMemoryGameEventStream();
        var game = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = stream.SubscribeAsync(game, cts.Token);
        var second = stream.SubscribeAsync(game, cts.Token);

        stream.Publish(new MovePlayed(game, At, 1, "white", "e4", "fen", 1, 5));
        stream.Publish(new GameFinished(game, At, "*", "aborted"));

        var a = await ReadAll(first);
        var b = await ReadAll(second);

        Assert.Equal(2, a.Count);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task EventsPublishedBeforeReadingAreNotLost()
    {
        var stream = new InMemoryGameEventStream();
        var game = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscription = stream.SubscribeAsync(game, cts.Token);

        stream.Publish(new AttemptRejected(game, At, 1, 1, "Qxf7", "Not a legal move."));
        stream.Publish(new GameFinished(game, At, "0-1", "forfeit-white"));

        var received = await ReadAll(subscription);

        Assert.IsType<AttemptRejected>(received[0]);
        Assert.IsType<GameFinished>(received[1]);
    }

    [Fact]
    public async Task GameFinishedEndsEverySubscriptionWithoutCancellation()
    {
        var stream = new InMemoryGameEventStream();
        var game = Guid.NewGuid();
        var subscription = stream.SubscribeAsync(game, CancellationToken.None);

        stream.Publish(new GameFinished(game, At, "1-0", "checkmate"));

        var received = await ReadAll(subscription).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("checkmate", Assert.IsType<GameFinished>(Assert.Single(received)).Termination);
    }

    [Fact]
    public async Task CancellingTheTokenEndsTheSubscriptionWithCancellation()
    {
        var stream = new InMemoryGameEventStream();
        var game = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var subscription = stream.SubscribeAsync(game, cts.Token);
        var reading = ReadAll(subscription);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Fact]
    public void PublishingToAGameWithoutSubscribersIsANoOp()
    {
        var stream = new InMemoryGameEventStream();

        stream.Publish(new GameFinished(Guid.NewGuid(), At, "*", "aborted"));
    }

    private static async Task<List<IGameEvent>> ReadAll(IAsyncEnumerable<IGameEvent> events)
    {
        var received = new List<IGameEvent>();
        await foreach (var e in events)
        {
            received.Add(e);
        }

        return received;
    }
}
