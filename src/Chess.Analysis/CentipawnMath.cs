namespace Chess.Analysis;

/// <summary>Centipawn rules of docs/RATING.md §4.</summary>
internal static class CentipawnMath
{
    /// <summary>Mate scores are mapped to ±1000 cp.</summary>
    public const int MateScore = 1000;

    /// <summary>Each move's loss is capped at 1000 cp.</summary>
    public const int LossCap = 1000;

    /// <summary>cpl = max(0, eval(best) − eval(played)), both from the mover's point of view, capped.</summary>
    public static int Loss(int bestForMover, int playedForMover) => Math.Clamp(bestForMover - playedForMover, 0, LossCap);
}
