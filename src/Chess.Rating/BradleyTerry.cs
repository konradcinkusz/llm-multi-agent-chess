namespace Chess.Rating;

// RATING.md §3: penalised Bradley–Terry with White advantage, fitted by Newton–Raphson.
internal static class BradleyTerry
{
    private const int MaxIterations = 10_000;
    private const int MaxHalvings = 60;
    private const double Tolerance = 1e-6;

    // Keeps the Hessian positive definite. Without a prior the common shift of all ratings is
    // undetermined, so this ridge is what makes the solve defined. It is far below the rating scale.
    private const double Ridge = 1e-9;

    // E = 1 / (1 + 10^(−d/400)) = sigmoid(K · d)
    private static readonly double K = Math.Log(10) / 400;

    // Sign of each parameter in d = R_white + η − R_black.
    private static readonly double[] Sign = [1, -1, 1];

    internal sealed record Fitted(IReadOnlyDictionary<string, double> Ratings, double WhiteAdvantage);

    private readonly record struct Game(int White, int Black, double Score);

    internal static Fitted Fit(IReadOnlyList<GameOutcome> games, RatingOptions options)
    {
        if (games.Count == 0)
        {
            return new Fitted(new Dictionary<string, double>(), 0);
        }

        var models = games.SelectMany(g => new[] { g.White, g.Black })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var index = models
            .Select((model, i) => (model, i))
            .ToDictionary(p => p.model, p => p.i, StringComparer.Ordinal);
        var n = models.Length;
        var players = games.Select(g => new Game(index[g.White], index[g.Black], g.WhiteScore)).ToArray();
        var precision = options.PriorSigma is { } sigma ? 1 / (sigma * sigma) : 0;

        // θ = (R_0 … R_{n−1}, η)
        var theta = new double[n + 1];
        var logLikelihood = LogLikelihood(players, theta, precision, n);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var (gradient, hessian) = Derivatives(players, theta, precision, n);
            var step = Solve(hessian, gradient);
            if (options.PriorSigma is null)
            {
                CentreRatings(step, n);
            }

            // Newton with step halving: the likelihood never decreases, so a bad start cannot diverge.
            var scale = 1.0;
            var accepted = false;
            for (var halving = 0; halving < MaxHalvings; halving++)
            {
                var candidate = Move(theta, step, scale);
                var candidateLogLikelihood = LogLikelihood(players, candidate, precision, n);
                if (candidateLogLikelihood >= logLikelihood)
                {
                    theta = candidate;
                    logLikelihood = candidateLogLikelihood;
                    accepted = true;
                    break;
                }

                scale /= 2;
            }

            if (!accepted || MaxAbs(step) * scale < Tolerance)
            {
                break;
            }
        }

        // Anchor: R_anchor = AnchorRating; without an anchor game, the mean of all R_i = AnchorRating.
        var ratings = theta[..n];
        var reference = options.AnchorModel is not null && index.TryGetValue(options.AnchorModel, out var anchor)
            ? ratings[anchor]
            : Mean(ratings);
        var shift = options.AnchorRating - reference;

        var anchored = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            anchored[models[i]] = ratings[i] + shift;
        }

        return new Fitted(anchored, theta[n]);
    }

    // ℓ = Σ_g [ s·ln E + (1−s)·ln(1−E) ] − Σ_i R_i² / (2σ²)
    private static double LogLikelihood(Game[] games, double[] theta, double precision, int n)
    {
        var sum = 0.0;
        foreach (var game in games)
        {
            var x = K * (theta[game.White] + theta[n] - theta[game.Black]);
            sum -= game.Score * SoftPlus(-x) + (1 - game.Score) * SoftPlus(x);
        }

        for (var i = 0; i < n; i++)
        {
            sum -= precision * theta[i] * theta[i] / 2;
        }

        return sum;
    }

    // Gradient of ℓ and A = −Hessian(ℓ), which is positive definite.
    private static (double[] Gradient, double[,] Hessian) Derivatives(Game[] games, double[] theta, double precision, int n)
    {
        var dim = n + 1;
        var gradient = new double[dim];
        var hessian = new double[dim, dim];
        foreach (var game in games)
        {
            var e = Sigmoid(K * (theta[game.White] + theta[n] - theta[game.Black]));
            var residual = K * (game.Score - e);
            var weight = K * K * e * (1 - e);
            var index = new[] { game.White, game.Black, n };
            for (var p = 0; p < 3; p++)
            {
                gradient[index[p]] += Sign[p] * residual;
                for (var q = 0; q < 3; q++)
                {
                    hessian[index[p], index[q]] += weight * Sign[p] * Sign[q];
                }
            }
        }

        for (var i = 0; i < n; i++)
        {
            gradient[i] -= precision * theta[i];
            hessian[i, i] += precision;
        }

        for (var i = 0; i < dim; i++)
        {
            hessian[i, i] += Ridge;
        }

        return (gradient, hessian);
    }

    // Cholesky solve of A·x = b for symmetric positive definite A.
    private static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var l = new double[n, n];
        for (var j = 0; j < n; j++)
        {
            var diagonal = a[j, j];
            for (var k = 0; k < j; k++)
            {
                diagonal -= l[j, k] * l[j, k];
            }

            l[j, j] = Math.Sqrt(diagonal);
            for (var i = j + 1; i < n; i++)
            {
                var s = a[i, j];
                for (var k = 0; k < j; k++)
                {
                    s -= l[i, k] * l[j, k];
                }

                l[i, j] = s / l[j, j];
            }
        }

        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = b[i];
            for (var k = 0; k < i; k++)
            {
                s -= l[i, k] * y[k];
            }

            y[i] = s / l[i, i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var s = y[i];
            for (var k = i + 1; k < n; k++)
            {
                s -= l[k, i] * x[k];
            }

            x[i] = s / l[i, i];
        }

        return x;
    }

    // The likelihood is invariant to a common shift of all ratings; the step must not drift along it.
    private static void CentreRatings(double[] step, int n)
    {
        var shift = Mean(step[..n]);
        for (var i = 0; i < n; i++)
        {
            step[i] -= shift;
        }
    }

    private static double[] Move(double[] theta, double[] step, double scale)
    {
        var moved = new double[theta.Length];
        for (var i = 0; i < theta.Length; i++)
        {
            moved[i] = theta[i] + scale * step[i];
        }

        return moved;
    }

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    // Numerically stable ln(1 + e^x).
    private static double SoftPlus(double x) => Math.Max(x, 0) + Math.Log(1 + Math.Exp(-Math.Abs(x)));

    private static double Mean(double[] values)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }

    private static double MaxAbs(double[] values)
    {
        var max = 0.0;
        foreach (var value in values)
        {
            max = Math.Max(max, Math.Abs(value));
        }

        return max;
    }
}
