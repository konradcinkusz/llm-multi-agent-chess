using Chess.Web.Components.Pages.Tournaments;

namespace Chess.Web.Components.Pages.Benchmark;

/// <summary>
/// The rules of <c>POST /api/benchmarks</c> (<c>BenchmarkRules.Validate</c> in Chess.Api), checked in the form so the user
/// sees a problem before anything is sent. The API still checks every request.
/// </summary>
public static class BenchmarkFormRules
{
    /// <summary>The highest concurrency the form offers: the same cap as a tournament (Arena:MaxConcurrentGames).</summary>
    public const int MaxConcurrencyLimit = TournamentFormRules.MaxConcurrencyLimit;

    /// <returns>The problems with the form, in the order they appear on it; empty when the form can be sent.</returns>
    /// <param name="candidate">The candidate's player id, with its effort; null when none is chosen.</param>
    /// <param name="opponentCount">The opponents the run will play: the chosen ones, or every default opponent.</param>
    public static IReadOnlyList<string> Problems(string name, string? candidate, int opponentCount, int gamesPerOpponent,
        int maxConcurrency)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("Give the benchmark a name.");
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            problems.Add("Choose a candidate model.");
        }

        if (opponentCount < 1)
        {
            problems.Add("Choose at least one opponent.");
        }

        // Colours alternate against each opponent, so each one needs an even number of games, at least one each way.
        if (gamesPerOpponent < 2 || gamesPerOpponent % 2 != 0)
        {
            problems.Add("Games per opponent must be an even number, at least 2, so that colours alternate.");
        }

        if (maxConcurrency < 1 || maxConcurrency > MaxConcurrencyLimit)
        {
            problems.Add($"Max concurrency must be between 1 and {MaxConcurrencyLimit}.");
        }

        return problems;
    }
}
