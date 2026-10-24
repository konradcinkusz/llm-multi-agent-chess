using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Chess.Api.Games;

/// <summary>
/// Fan-out of game events to the live subscribers of each game. Nothing is replayed: a subscriber gets the events
/// published after it subscribed. Publishing never waits, because each subscriber has its own queue. The subscribers
/// of a game end after its <see cref="GameFinished"/>.
/// </summary>
public sealed class InMemoryGameEventStream : IGameEventStream
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<Channel<IGameEvent>>> _subscribers = [];

    public void Publish(IGameEvent e)
    {
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(e.GameId, out var channels))
            {
                return;
            }

            foreach (var channel in channels)
            {
                channel.Writer.TryWrite(e);
            }

            if (e is GameFinished)
            {
                _subscribers.Remove(e.GameId);
                foreach (var channel in channels)
                {
                    channel.Writer.TryComplete();
                }
            }
        }
    }

    /// <summary>
    /// Subscribes at once, not when the sequence is first read, so nothing published in between is lost. Enumerate the
    /// sequence or cancel <paramref name="ct"/> to release the subscription.
    /// </summary>
    public IAsyncEnumerable<IGameEvent> SubscribeAsync(Guid gameId, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<IGameEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(gameId, out var channels))
            {
                _subscribers[gameId] = channels = [];
            }

            channels.Add(channel);
        }

        var registration = ct.Register(() => Remove(gameId, channel));
        return Read(gameId, channel, registration, ct);
    }

    private async IAsyncEnumerable<IGameEvent> Read(
        Guid gameId,
        Channel<IGameEvent> channel,
        CancellationTokenRegistration registration,
        [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct))
            {
                yield return e;
            }
        }
        finally
        {
            registration.Dispose();
            Remove(gameId, channel);
        }
    }

    private void Remove(Guid gameId, Channel<IGameEvent> channel)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(gameId, out var channels))
            {
                channels.Remove(channel);
                if (channels.Count == 0)
                {
                    _subscribers.Remove(gameId);
                }
            }

            channel.Writer.TryComplete();
        }
    }
}
