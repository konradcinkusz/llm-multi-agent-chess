namespace Chess.Rating;

public sealed record GameOutcome(string White, string Black, double WhiteScore);   // 1, 0.5, 0
public sealed record PlayerMoveStats(string Model, int Moves, int FirstAttemptLegal, double? AverageCentipawnLoss);
public sealed record RatingOptions(
    string? AnchorModel = "random", double AnchorRating = 0, double? PriorSigma = 350,
    int BootstrapSamples = 200, int Seed = 20261024, int ProvisionalBelowGames = 20,
    double ResultWeight = 0.75, double MoveQualityWeight = 0.6);
// ArenaScoreLow / ArenaScoreHigh are the 2.5 / 97.5 percentiles of the arena score over the bootstrap (RATING.md §6).
public sealed record ModelRating(string Model, double Rating, double CiLow, double CiHigh,
    int Games, double Score, double Legality, double? MoveQuality, double Quality,
    double ArenaScore, bool Provisional, double? ArenaScoreLow = null, double? ArenaScoreHigh = null);

// RATING.md §6: Score is the published leaderboard (arena score, then rating); Conservative ranks by the lower bound of the arena score.
public enum RatingOrder
{
    Score,
    Conservative,
}
public sealed record PairwiseCoefficient(string A, string B, double ExpectedScore, double QualityShare,
    double Rqc, double RqcLow, double RqcHigh, double EloEquivalent,
    int HeadToHeadGames, double? HeadToHeadScore);
public sealed record RatingReport(IReadOnlyList<ModelRating> Models, double WhiteAdvantage, int Games);

// Signatures frozen by T0.2; bodies implemented by T1.6 in RatingEngine (RATING.md §3–§6).
public static class RatingCalculator
{
    public static RatingReport Compute(IReadOnlyList<GameOutcome> games,
        IReadOnlyList<PlayerMoveStats> stats, RatingOptions options) => RatingEngine.Compute(games, stats, options);
    public static PairwiseCoefficient Pairwise(RatingReport report, IReadOnlyList<GameOutcome> games, string a, string b)
        => RatingEngine.Pairwise(report, games, a, b);

    /// <summary>The same report with its models in the given order. Only the order changes; no number is recomputed.</summary>
    public static RatingReport Order(RatingReport report, RatingOrder order)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report with { Models = RatingEngine.Sort(report.Models, order) };
    }
}
