namespace Chess.Analysis;

public sealed record MoveQuality(int Ply, int CentipawnLoss, string BestSan);
public interface IMoveQualityAnalyzer
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct);
    /// <summary>Centipawns from White's point of view (mate = ±1000); null when unavailable.</summary>
    Task<int?> EvaluateAsync(string fen, CancellationToken ct);
}
