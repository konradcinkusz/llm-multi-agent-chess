namespace Chess.Api.Games;

public interface IGameRunner
{
    ValueTask EnqueueAsync(Guid gameId, CancellationToken ct);
    bool TryAbort(Guid gameId);
}

public interface IGameEvent { Guid GameId { get; } DateTimeOffset At { get; } }
public sealed record MoveStarted(Guid GameId, DateTimeOffset At, int Ply, string Color, string ModelId) : IGameEvent;
public sealed record AttemptRejected(Guid GameId, DateTimeOffset At, int Ply, int Attempt, string? RawReply, string Reason) : IGameEvent;
public sealed record MovePlayed(Guid GameId, DateTimeOffset At, int Ply, string Color, string San, string Fen, int Attempts, long LatencyMs) : IGameEvent;
public sealed record GameFinished(Guid GameId, DateTimeOffset At, string Result, string Termination) : IGameEvent;

public interface IGameEventStream
{
    void Publish(IGameEvent e);
    IAsyncEnumerable<IGameEvent> SubscribeAsync(Guid gameId, CancellationToken ct);
}
