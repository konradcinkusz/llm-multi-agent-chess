namespace Chess.Analysis;

/// <summary>A UCI engine that can search one position at a fixed depth. Implemented by the process wrapper and by test doubles.</summary>
internal interface IUciEngine : IAsyncDisposable
{
    Task<UciSearch> SearchAsync(string fen, IReadOnlyList<string> uciMoves, int depth, CancellationToken ct);
}
