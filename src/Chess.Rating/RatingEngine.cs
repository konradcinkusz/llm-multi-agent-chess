namespace Chess.Rating;

// RATING.md §4–§6 around the fit: canonical game order, seeded bootstrap, move statistics,
// relative coefficient, arena score and leaderboard order.
internal static class RatingEngine
{
    private const string RandomModel = "random";
    private const double ProvisionalRatingWidth = 200;
    private const double LowerPercentile = 0.025;
    private const double UpperPercentile = 0.975;

    // AS of a model with no opponent: the field average.
    private const double ArenaCentre = 50;

    private sealed record Tally(int Games, double Points);

    private sealed record Skill(double Legality, double? MoveQuality, double Quality);

    public static RatingReport Compute(IReadOnlyList<GameOutcome> games, IReadOnlyList<PlayerMoveStats> stats, RatingOptions options) =>
        Compute(games, stats, options, CancellationToken.None);

    public static RatingReport Compute(
        IReadOnlyList<GameOutcome> games, IReadOnlyList<PlayerMoveStats> stats, RatingOptions options, CancellationToken ct) =>
        ComputeCore(games, stats, options, ct).Report;

    // The report and every ordered pair of its models, from one bootstrap when the options are the defaults Pairwise uses.
    public static RatingWithMatrix ComputeWithMatrix(
        IReadOnlyList<GameOutcome> games, IReadOnlyList<PlayerMoveStats> stats, RatingOptions options, CancellationToken ct)
    {
        var core = ComputeCore(games, stats, options, ct);
        if (core.Ordered.Count == 0)
        {
            return new RatingWithMatrix(core.Report, []);
        }

        var samples = SameBootstrap(options, PairwiseDefaults) ? core.Samples : Bootstrap(core.Ordered, PairwiseDefaults, ct);
        return new RatingWithMatrix(core.Report, MatrixCells(core.Report, core.Ordered, samples, ct));
    }

    private sealed record Core(RatingReport Report, List<GameOutcome> Ordered, List<BradleyTerry.Fitted> Samples);

    private static Core ComputeCore(
        IReadOnlyList<GameOutcome> games, IReadOnlyList<PlayerMoveStats> stats, RatingOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var ordered = Canonical(games);
        if (ordered.Count == 0)
        {
            return new Core(new RatingReport(Array.Empty<ModelRating>(), 0, 0), ordered, []);
        }

        var fit = BradleyTerry.Fit(ordered, options, ct);
        var samples = Bootstrap(ordered, options, ct);
        var models = fit.Ratings.Keys.Order(StringComparer.Ordinal).ToArray();
        var tallies = TallyByModel(ordered);
        var moves = MoveStatistics(stats);
        var skills = models.ToDictionary(m => m, m => SkillOf(m, moves, options.MoveQualityWeight), StringComparer.Ordinal);
        var arena = ArenaScores(models, fit.Ratings, skills, options.ResultWeight);
        var arenaSamples = ArenaSamples(samples, skills, options.ResultWeight, ct);

        var rows = models.Select(model =>
        {
            var tally = tallies[model];
            var skill = skills[model];
            var rating = fit.Ratings[model];
            var (low, high) = Interval(RatingSamples(model, samples), rating);
            var (arenaLow, arenaHigh) = Interval(arenaSamples.TryGetValue(model, out var values) ? values : [], arena[model]);
            return new ModelRating(
                model, rating, low, high,
                tally.Games, tally.Points / tally.Games,
                skill.Legality, skill.MoveQuality, skill.Quality,
                arena[model],
                tally.Games < options.ProvisionalBelowGames || high - low > ProvisionalRatingWidth,
                arenaLow, arenaHigh);
        }).ToList();

        return new Core(new RatingReport(Sort(rows, RatingOrder.Score), fit.WhiteAdvantage, ordered.Count), ordered, samples);
    }

    // Score: today's leaderboard (RATING.md §6). Conservative: the lower bound of the arena score first, so a model with more
    // games (narrower interval) outranks one with the same point score and fewer games. Provisional models sort last in both.
    // A row without bounds (built outside Compute) uses its arena score as its lower bound.
    public static List<ModelRating> Sort(IEnumerable<ModelRating> rows, RatingOrder order) => order switch
    {
        RatingOrder.Score => rows
            .OrderBy(r => r.Provisional)
            .ThenByDescending(r => r.ArenaScore)
            .ThenByDescending(r => r.Rating)
            .ThenBy(r => r.Model, StringComparer.Ordinal)
            .ToList(),
        RatingOrder.Conservative => rows
            .OrderBy(r => r.Provisional)
            .ThenByDescending(r => r.ArenaScoreLow ?? r.ArenaScore)
            .ThenByDescending(r => r.ArenaScore)
            .ThenByDescending(r => r.Rating)
            .ThenBy(r => r.Model, StringComparer.Ordinal)
            .ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown ranking order."),
    };

    public static PairwiseCoefficient Pairwise(RatingReport report, IReadOnlyList<GameOutcome> games, string a, string b)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(games);

        var rowA = Find(report, a, nameof(a));
        var rowB = Find(report, b, nameof(b));

        // The signature carries no options, so the bootstrap and ResultWeight are the defaults.
        var ordered = Canonical(games);
        return PairwiseOf(rowA, rowB, ordered, Bootstrap(ordered, PairwiseDefaults, CancellationToken.None));
    }

    // Every ordered pair of distinct models of the report, a-major in the report's order: one bootstrap for all cells,
    // each cell identical to Pairwise(report, games, a, b).
    public static IReadOnlyList<PairwiseCoefficient> Matrix(
        RatingReport report, IReadOnlyList<GameOutcome> games, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(games);

        var ordered = Canonical(games);
        return MatrixCells(report, ordered, Bootstrap(ordered, PairwiseDefaults, ct), ct);
    }

    private static readonly RatingOptions PairwiseDefaults = new();

    // The resamples depend on these fields only (games, seed, sample count, and the fit's prior and anchor).
    private static bool SameBootstrap(RatingOptions x, RatingOptions y) =>
        x.BootstrapSamples == y.BootstrapSamples && x.Seed == y.Seed && x.PriorSigma == y.PriorSigma
        && x.AnchorModel == y.AnchorModel && x.AnchorRating == y.AnchorRating;

    private static List<PairwiseCoefficient> MatrixCells(
        RatingReport report, IReadOnlyList<GameOutcome> ordered, IReadOnlyList<BradleyTerry.Fitted> samples, CancellationToken ct)
    {
        var cells = new List<PairwiseCoefficient>();
        foreach (var rowA in report.Models)
        {
            foreach (var rowB in report.Models)
            {
                if (rowA.Model == rowB.Model)
                {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                cells.Add(PairwiseOf(rowA, rowB, ordered, samples));
            }
        }

        return cells;
    }

    private static PairwiseCoefficient PairwiseOf(
        ModelRating rowA, ModelRating rowB, IReadOnlyList<GameOutcome> ordered, IReadOnlyList<BradleyTerry.Fitted> samples)
    {
        var a = rowA.Model;
        var b = rowB.Model;
        var expected = ExpectedScore(rowA.Rating, rowB.Rating);
        var share = QualityShare(rowA.Quality, rowB.Quality);
        var rqc = Rqc(rowA.Rating, rowB.Rating, rowA.Quality, rowB.Quality, PairwiseDefaults.ResultWeight);

        var rqcSamples = new List<double>();
        foreach (var sample in samples)
        {
            if (sample.Ratings.TryGetValue(a, out var ratingA) && sample.Ratings.TryGetValue(b, out var ratingB))
            {
                rqcSamples.Add(Rqc(ratingA, ratingB, rowA.Quality, rowB.Quality, PairwiseDefaults.ResultWeight));
            }
        }

        var (low, high) = Interval(rqcSamples, rqc);
        var direct = ordered.Where(g => (g.White == a && g.Black == b) || (g.White == b && g.Black == a)).ToList();
        double? headToHead = direct.Count == 0
            ? null
            : direct.Sum(g => g.White == a ? g.WhiteScore : 1 - g.WhiteScore) / direct.Count;

        return new PairwiseCoefficient(
            a, b, expected, share, rqc, low, high,
            400 * Math.Log10(rqc / (1 - rqc)),
            direct.Count, headToHead);
    }

    // The fit is a function of the games as a multiset, so the order is fixed before any sum.
    private static List<GameOutcome> Canonical(IReadOnlyList<GameOutcome> games)
    {
        foreach (var game in games)
        {
            if (game.WhiteScore is not (>= 0 and <= 1))
            {
                throw new ArgumentException($"WhiteScore must be between 0 and 1, was {game.WhiteScore}.", nameof(games));
            }
        }

        return games
            .OrderBy(g => g.White, StringComparer.Ordinal)
            .ThenBy(g => g.Black, StringComparer.Ordinal)
            .ThenBy(g => g.WhiteScore)
            .ToList();
    }

    private static Dictionary<string, Tally> TallyByModel(IReadOnlyList<GameOutcome> games)
    {
        var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var game in games)
        {
            Add(tallies, game.White, game.WhiteScore);
            Add(tallies, game.Black, 1 - game.WhiteScore);
        }

        return tallies;

        static void Add(Dictionary<string, Tally> dictionary, string model, double points)
        {
            dictionary.TryGetValue(model, out var tally);
            dictionary[model] = new Tally((tally?.Games ?? 0) + 1, (tally?.Points ?? 0) + points);
        }
    }

    private static Dictionary<string, PlayerMoveStats> MoveStatistics(IReadOnlyList<PlayerMoveStats> stats)
    {
        var result = new Dictionary<string, PlayerMoveStats>(StringComparer.Ordinal);
        foreach (var entry in stats)
        {
            if (entry.Moves < 0 || entry.FirstAttemptLegal < 0 || entry.FirstAttemptLegal > entry.Moves)
            {
                throw new ArgumentException($"Inconsistent move statistics for '{entry.Model}'.", nameof(stats));
            }

            if (!result.TryAdd(entry.Model, entry))
            {
                throw new ArgumentException($"Duplicate move statistics for '{entry.Model}'.", nameof(stats));
            }
        }

        return result;
    }

    // L = first-attempt-legal / moves. A model with no recorded moves has no legality to show, so it gets
    // none: the random player is legal by construction, anyone else is 0 (no free move; RATING.md §2).
    // MQ = 1 / (1 + ACPL/100) when Stockfish data exists; Q blends MQ and L, or is L alone (RATING.md §4).
    private static Skill SkillOf(string model, IReadOnlyDictionary<string, PlayerMoveStats> moves, double moveQualityWeight)
    {
        var stat = moves.GetValueOrDefault(model);
        var legality = stat is { Moves: > 0 } s
            ? (double)s.FirstAttemptLegal / s.Moves
            : model == RandomModel ? 1.0 : 0.0;
        double? moveQuality = stat?.AverageCentipawnLoss is { } acpl ? 1 / (1 + acpl / 100) : null;
        var quality = moveQuality is { } mq ? moveQualityWeight * mq + (1 - moveQualityWeight) * legality : legality;
        return new Skill(legality, moveQuality, quality);
    }

    // E(A,B) = 1 / (1 + 10^((R_B − R_A)/400)); QS = Q_A / (Q_A + Q_B); RQC = w·E + (1−w)·QS (RATING.md §5).
    private static double ExpectedScore(double ratingA, double ratingB) => 1 / (1 + Math.Pow(10, (ratingB - ratingA) / 400));

    private static double QualityShare(double qualityA, double qualityB) => qualityA + qualityB > 0 ? qualityA / (qualityA + qualityB) : 0.5;

    private static double Rqc(double ratingA, double ratingB, double qualityA, double qualityB, double resultWeight) =>
        resultWeight * ExpectedScore(ratingA, ratingB) + (1 - resultWeight) * QualityShare(qualityA, qualityB);

    // AS_i = 100 · mean over j ≠ i of RQC(i, j)
    private static Dictionary<string, double> ArenaScores(
        string[] models, IReadOnlyDictionary<string, double> ratings, IReadOnlyDictionary<string, Skill> skills, double resultWeight)
    {
        var arena = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in models)
        {
            if (models.Length == 1)
            {
                arena[a] = ArenaCentre;
                continue;
            }

            var total = 0.0;
            foreach (var b in models)
            {
                if (b != a)
                {
                    total += Rqc(ratings[a], ratings[b], skills[a].Quality, skills[b].Quality, resultWeight);
                }
            }

            arena[a] = 100 * total / (models.Length - 1);
        }

        return arena;
    }

    // RATING.md §6: each bootstrap resample is refit and anchored like the point estimate. The resamples are drawn first,
    // in sequence from one seeded generator, so the draws are the ones a sequential loop would make; the fits are
    // independent of each other and run in parallel, each landing at its own index, so the result does not depend on the schedule.
    private static List<BradleyTerry.Fitted> Bootstrap(IReadOnlyList<GameOutcome> games, RatingOptions options, CancellationToken ct)
    {
        if (games.Count == 0 || options.BootstrapSamples == 0)
        {
            return [];
        }

        var random = new Random(options.Seed);
        var draws = new int[options.BootstrapSamples][];
        for (var b = 0; b < draws.Length; b++)
        {
            var draw = new int[games.Count];
            for (var i = 0; i < draw.Length; i++)
            {
                draw[i] = random.Next(games.Count);
            }

            draws[b] = draw;
        }

        var fits = new BradleyTerry.Fitted[draws.Length];
        Parallel.For(0, draws.Length, new ParallelOptions { CancellationToken = ct }, b =>
        {
            var resample = new GameOutcome[games.Count];
            for (var i = 0; i < resample.Length; i++)
            {
                resample[i] = games[draws[b][i]];
            }

            fits[b] = BradleyTerry.Fit(resample, options, ct);
        });

        return [.. fits];
    }

    private static List<double> RatingSamples(string model, IEnumerable<BradleyTerry.Fitted> samples) =>
        samples.Where(s => s.Ratings.ContainsKey(model)).Select(s => s.Ratings[model]).ToList();

    // RATING.md §6: the arena score of each model on each resample. Each resample has its own field, so a model is
    // scored only against the models that resample rated; a model absent from a resample has no value there.
    private static Dictionary<string, List<double>> ArenaSamples(
        IReadOnlyList<BradleyTerry.Fitted> samples, IReadOnlyDictionary<string, Skill> skills, double resultWeight, CancellationToken ct)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            ct.ThrowIfCancellationRequested();
            var models = sample.Ratings.Keys.Order(StringComparer.Ordinal).ToArray();
            foreach (var (model, score) in ArenaScores(models, sample.Ratings, skills, resultWeight))
            {
                if (!values.TryGetValue(model, out var list))
                {
                    list = [];
                    values[model] = list;
                }

                list.Add(score);
            }
        }

        return values;
    }

    // The percentile interval of the bootstrap. Without samples the interval collapses to the point estimate.
    private static (double Low, double High) Interval(List<double> values, double point)
    {
        if (values.Count == 0)
        {
            return (point, point);
        }

        values.Sort();
        return (Percentile(values, LowerPercentile), Percentile(values, UpperPercentile));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        var position = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static ModelRating Find(RatingReport report, string model, string parameter) =>
        report.Models.FirstOrDefault(m => m.Model == model)
        ?? throw new ArgumentException($"Model '{model}' is not in the report.", parameter);

    private static void Validate(RatingOptions options)
    {
        if (options.BootstrapSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BootstrapSamples must not be negative.");
        }

        if (options.PriorSigma is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PriorSigma must be positive or null.");
        }

        if (options.ResultWeight is < 0 or > 1 || options.MoveQualityWeight is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Weights must be between 0 and 1.");
        }
    }
}
