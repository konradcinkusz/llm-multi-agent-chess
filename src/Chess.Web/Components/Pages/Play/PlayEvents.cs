namespace Chess.Web.Components.Pages.Play;

// The IGameEvent records of 03 §3.3 as they arrive on the SSE stream (camelCase JSON, `event:` = type name).
// Copies, as in Chess.Web.Api; only the fields the live page shows.

internal sealed record MoveStartedEvent(Guid GameId, DateTimeOffset At, int Ply, string Color, string ModelId);

internal sealed record AttemptRejectedEvent(Guid GameId, DateTimeOffset At, int Ply, int Attempt, string? RawReply, string Reason);

internal sealed record MovePlayedEvent(Guid GameId, DateTimeOffset At, int Ply, string Color, string San, string Fen,
    int Attempts, long LatencyMs);

internal sealed record GameFinishedEvent(Guid GameId, DateTimeOffset At, string Result, string Termination);
