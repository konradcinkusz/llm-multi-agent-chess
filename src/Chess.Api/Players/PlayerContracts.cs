namespace Chess.Api.Players;

public sealed record PlayerDescriptor(string Kind, string ModelId);        // Kind: "llm" | "random"
public sealed record MoveRequest(Guid GameId, int Ply, string Fen,
    IReadOnlyList<string> LegalSan, IReadOnlyList<string> HistorySan);
public sealed record MoveAttempt(int Number, string Prompt, string? RawReply, string? ParsedSan,
    bool Legal, TimeSpan Latency, int? InputTokens, int? OutputTokens, string? Error,
    string? Reasoning = null, int? ReasoningTokens = null, int? CachedInputTokens = null,
    double? AiCredits = null, string? RejectionKind = null);
public sealed record PlayerMove(string? San, IReadOnlyList<MoveAttempt> Attempts, bool Forfeit);

public interface IChessPlayer : IAsyncDisposable
{
    PlayerDescriptor Descriptor { get; }
    Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct);
}

public interface IChessPlayerFactory
{
    Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Chess.Engine.Side side, CancellationToken ct);
}
