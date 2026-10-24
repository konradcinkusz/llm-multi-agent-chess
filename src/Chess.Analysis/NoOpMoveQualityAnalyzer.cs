namespace Chess.Analysis;

/// <summary>Used when no Stockfish binary exists: move quality is disabled and nothing is written (P8).</summary>
public sealed class NoOpMoveQualityAnalyzer : IMoveQualityAnalyzer
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MoveQuality>>([]);

    public Task<int?> EvaluateAsync(string fen, CancellationToken ct) => Task.FromResult<int?>(null);
}
