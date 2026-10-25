using Chess.Api.Contracts;
using Chess.Api.Ratings;
using Chess.Rating;

namespace Chess.Api.Benchmarks;

/// <summary>
/// One game of a benchmark, as the report reads it: the candidate's colour, the result and termination, the candidate's
/// first-try legal moves, and what each side's replies cost.
/// </summary>
public sealed record BenchmarkGame(string Opponent, bool CandidateIsWhite, string Result, string? Termination,
    int CandidateMoves, int CandidateFirstAttemptLegal, SideUsageDto Candidate, SideUsageDto OpponentUsage);

/// <summary>
/// The numbers of a benchmark report (03 §12.2, RATING.md §6). Pure: the caller loads the games, the global rating and the
/// fingerprint; this only adds them up. A game counts as decided when it has a result; an aborted or unfinished game
/// keeps <c>*</c> and is left out of the score, but its replies still count towards the cost.
/// </summary>
public static class BenchmarkReportBuilder
{
    /// <summary>The bootstrap resamples behind each Elo interval.</summary>
    public const int BootstrapSamples = 1000;

    /// <summary>Elo points per factor of ten in the odds (the logistic scale of RATING.md §3).</summary>
    private const double EloScale = 400;

    private const string Unfinished = "*";

    public static BenchmarkReportDto Build(BenchmarkDto benchmark, IReadOnlyList<BenchmarkGame> games, RatingReport rating,
        IReadOnlyList<GameOutcome> ratedOutcomes, BenchmarkFingerprintDto fingerprint)
    {
        var candidate = benchmark.Candidate;
        var opponents = benchmark.Opponents
            .Select(opponent => OpponentOf(candidate, opponent, games.Where(g => g.Opponent == opponent).ToList(), rating, ratedOutcomes))
            .ToArray();

        var decided = games.Where(IsDecided).ToList();
        var scores = decided.Select(PointsOf).ToList();
        var model = rating.Models.FirstOrDefault(m => m.Model == candidate);
        var summary = new BenchmarkSummaryDto(
            Games: decided.Count,
            Score: Mean(scores),
            Rating: model?.Rating,
            RatingLow: model?.CiLow,
            RatingHigh: model?.CiHigh,
            ArenaScore: model?.ArenaScore,
            Provisional: model?.Provisional ?? true,
            CandidateTokens: games.Sum(g => g.Candidate.InputTokens + g.Candidate.OutputTokens),
            CandidateEstimatedUsd: SumKnown(games.Select(g => g.Candidate.EstimatedUsd)),
            TotalEstimatedUsd: SumKnown(games.SelectMany(g => new[] { g.Candidate.EstimatedUsd, g.OpponentUsage.EstimatedUsd })));

        return new BenchmarkReportDto(benchmark, opponents, summary, fingerprint);
    }

    /// <summary>
    /// The Elo difference of a candidate with the given per-game scores (1, ½, 0), and its 95 % bootstrap interval (2.5 and
    /// 97.5 percentiles of resampled games). A score of 0 or 1 is an unbounded difference: the point and any bound that is
    /// unbounded are null. Deterministic: the resamples use the rating seed (RATING.md §5).
    /// </summary>
    public static (double? Diff, double? Low, double? High) EloDifference(IReadOnlyList<double> scores)
    {
        if (scores.Count == 0)
        {
            return (null, null, null);
        }

        var random = new Random(new RatingOptions().Seed);
        var samples = new double[BootstrapSamples];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var total = 0d;
            for (var game = 0; game < scores.Count; game++)
            {
                total += scores[random.Next(scores.Count)];
            }

            samples[sample] = EloOf(total / scores.Count);
        }

        Array.Sort(samples);
        return (Finite(EloOf(Mean(scores))), Finite(Percentile(samples, 0.025)), Finite(Percentile(samples, 0.975)));
    }

    private static BenchmarkOpponentDto OpponentOf(string candidate, string opponent, IReadOnlyList<BenchmarkGame> games,
        RatingReport rating, IReadOnlyList<GameOutcome> ratedOutcomes)
    {
        var decided = games.Where(IsDecided).ToList();
        var scores = decided.Select(PointsOf).ToList();
        var (diff, low, high) = EloDifference(scores);
        var (rqc, rqcLow, rqcHigh) = RqcOf(candidate, opponent, rating, ratedOutcomes);
        var moves = games.Sum(g => g.CandidateMoves);
        var firstTryLegal = games.Sum(g => g.CandidateFirstAttemptLegal);

        return new BenchmarkOpponentDto(
            Opponent: opponent,
            Games: decided.Count,
            Wins: scores.Count(s => s == 1),
            Draws: scores.Count(s => s == 0.5),
            Losses: scores.Count(s => s == 0),
            ForfeitLosses: decided.Count(g => g.Termination == (g.CandidateIsWhite ? "forfeit-white" : "forfeit-black")),
            Score: Mean(scores),
            EloDiff: diff,
            EloDiffLow: low,
            EloDiffHigh: high,
            Rqc: rqc,
            RqcLow: rqcLow,
            RqcHigh: rqcHigh,
            CandidateFirstAttemptLegalRate: moves == 0 ? 0 : (double)firstTryLegal / moves,
            CandidateEstimatedUsd: SumKnown(games.Select(g => g.Candidate.EstimatedUsd)));
    }

    // RQC (RATING.md §7) is read from the global fit, so it needs both players in the rating; otherwise it is unknown.
    private static (double? Rqc, double? Low, double? High) RqcOf(string candidate, string opponent, RatingReport rating,
        IReadOnlyList<GameOutcome> ratedOutcomes)
    {
        if (!rating.Models.Any(m => m.Model == candidate) || !rating.Models.Any(m => m.Model == opponent))
        {
            return (null, null, null);
        }

        var pair = RatingCalculator.Pairwise(rating, ratedOutcomes, candidate, opponent);
        return (pair.Rqc, pair.RqcLow, pair.RqcHigh);
    }

    private static bool IsDecided(BenchmarkGame game) => game.Result != Unfinished;

    /// <summary>The candidate's points in a decided game: 1 for a win, ½ for a draw, 0 for a loss.</summary>
    private static double PointsOf(BenchmarkGame game)
    {
        var whitePoints = game.Result switch
        {
            "1-0" => 1d,
            "1/2-1/2" => 0.5,
            "0-1" => 0d,
            _ => throw new InvalidOperationException($"Benchmark game has unexpected result '{game.Result}'."),
        };
        return game.CandidateIsWhite ? whitePoints : 1 - whitePoints;
    }

    // Logit scale: 400 × log10(s / (1 − s)). A score of 0 or 1 has no finite difference.
    private static double EloOf(double score) => score switch
    {
        <= 0 => double.NegativeInfinity,
        >= 1 => double.PositiveInfinity,
        _ => EloScale * Math.Log10(score / (1 - score)),
    };

    // Nearest rank on the sorted resamples; an unbounded value stays infinite and is reported as null by the caller.
    private static double Percentile(double[] sorted, double fraction) =>
        sorted[(int)Math.Round(fraction * (sorted.Length - 1))];

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private static double Mean(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();

    /// <summary>The sum of the known amounts; null when none is known.</summary>
    private static double? SumKnown(IEnumerable<double?> amounts)
    {
        var known = amounts.OfType<double>().ToList();
        return known.Count == 0 ? null : known.Sum();
    }
}
