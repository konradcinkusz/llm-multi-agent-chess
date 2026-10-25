using Chess.Api.Catalog;
using Chess.Api.Contracts;

namespace Chess.Api.Benchmarks;

/// <summary>The checks a benchmark request must pass before any game is created. Keys are the JSON property names.</summary>
public static class BenchmarkRules
{
    /// <summary>Games each opponent plays against the candidate when a request does not say otherwise (03 §12.2).</summary>
    public const int DefaultGamesPerOpponent = 30;

    /// <summary>
    /// The opponents a run plays: the request's list when it names any, else every enabled model in the catalog except the
    /// candidate's model (whatever its effort) and Auto (03 §12.2). The random player is an opponent like any other.
    /// </summary>
    public static IReadOnlyList<string> OpponentsOf(CreateBenchmarkRequest request, ModelListing listing)
    {
        if (request.Opponents is { Count: > 0 } named)
        {
            return named;
        }

        var candidateModel = ModelVariant.BaseOf(request.Candidate ?? "");
        return listing.Models
            .Where(m => m.Enabled && m.Id != candidateModel && !ModelCatalog.IsAutoModel(m.Id))
            .Select(m => m.Id)
            .ToArray();
    }

    /// <returns>The problems found, by property name; empty when the request is valid.</returns>
    public static Dictionary<string, string[]> Validate(CreateBenchmarkRequest request, IReadOnlyList<string> opponents,
        ModelListing listing, int maxConcurrentGames)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["A benchmark needs a name."];
        }

        var candidate = request.Candidate;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            errors["candidate"] = ["A benchmark needs a candidate model."];
        }
        else if (ModelVariant.BaseOf(candidate) == ModelCatalog.RandomModelId)
        {
            errors["candidate"] = ["The random player cannot be a candidate."];
        }
        else if (ModelVariant.Problem(candidate, listing) is { } candidateProblem)
        {
            errors["candidate"] = [candidateProblem];
        }

        if (opponents.Count == 0)
        {
            errors["opponents"] = ["No opponent is available: enable a model, or name the opponents."];
        }
        else if (opponents.Any(string.IsNullOrWhiteSpace))
        {
            errors["opponents"] = ["Every opponent needs an id."];
        }
        else if (opponents.Distinct().Count() != opponents.Count)
        {
            errors["opponents"] = ["Each opponent may appear only once."];
        }
        else if (opponents.Contains(candidate))
        {
            errors["opponents"] = ["The candidate cannot be its own opponent."];
        }
        else
        {
            var problems = opponents.Select(o => ModelVariant.Problem(o, listing)).OfType<string>().ToArray();
            if (problems.Length > 0)
            {
                errors["opponents"] = problems;
            }
        }

        // Colours alternate against each opponent, so the games per opponent must be even (at least one each way).
        if (request.GamesPerOpponent < 2 || request.GamesPerOpponent % 2 != 0)
        {
            errors["gamesPerOpponent"] = ["Games per opponent must be an even number, at least 2, so that colours alternate."];
        }

        if (request.MaxConcurrency < 1 || request.MaxConcurrency > maxConcurrentGames)
        {
            errors["maxConcurrency"] = [$"Max concurrency must be between 1 and {maxConcurrentGames} (Arena:MaxConcurrentGames)."];
        }

        return errors;
    }
}
