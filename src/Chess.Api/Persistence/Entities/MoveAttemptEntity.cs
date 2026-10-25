namespace Chess.Api.Persistence.Entities;

/// <summary>
/// One row of <c>move_attempts</c>: every LLM reply, legal or not, keyed by (game, ply, number). The usage columns (03 §10)
/// are null when the runtime reported none, and always null for the random side.
/// </summary>
public sealed class MoveAttemptEntity
{
    public Guid GameId { get; set; }
    public int Ply { get; set; }
    public int Number { get; set; }
    public string Prompt { get; set; } = "";
    public string? RawReply { get; set; }
    public string? ParsedSan { get; set; }
    public bool Legal { get; set; }
    public long LatencyMs { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reasoning { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public double? AiCredits { get; set; }

    /// <summary>03 §10.3: <c>unparseable | illegal | ambiguous | timeout | error</c>; null when the attempt was legal.</summary>
    public string? RejectionKind { get; set; }
}
