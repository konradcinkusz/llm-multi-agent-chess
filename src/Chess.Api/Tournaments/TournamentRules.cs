using Chess.Api.Catalog;
using Chess.Api.Contracts;

namespace Chess.Api.Tournaments;

/// <summary>The checks a tournament request must pass before any game is created. Keys are the JSON property names.</summary>
public static class TournamentRules
{
    /// <summary>The most models one round robin may have.</summary>
    public const int MaxModels = 16;

    /// <summary>The most games each pair of models may play.</summary>
    public const int MaxGamesPerPair = 100;

    /// <summary>The most games one run (a round robin, or a benchmark) may create in all; every game is rated and may cost.</summary>
    public const int MaxGames = 2000;

    /// <summary>
    /// What stops a run that plays an LLM while the gateway is not ready, or null. The same answer as <c>POST /api/games</c> gives
    /// (503): a run accepted without a gateway would only turn into aborted games. A run of random players needs no gateway.
    /// </summary>
    public static string? GatewayProblem(IEnumerable<string?> playerIds, ModelListing listing) =>
        listing.Warning is { } warning && playerIds.Any(id => !string.IsNullOrWhiteSpace(id) && ModelVariant.BaseOf(id) != ModelCatalog.RandomModelId)
            ? $"{warning}. Run `copilot` and log in, then try again."
            : null;

    /// <param name="listing">When given, a player id that names an effort (model@effort) is checked against the model's efforts.</param>
    /// <returns>The problems found, by property name; empty when the request is valid.</returns>
    public static Dictionary<string, string[]> Validate(CreateTournamentRequest request, int maxConcurrentGames, ModelListing? listing = null)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["A tournament needs a name."];
        }

        var models = request.Models ?? [];
        if (models.Count < 2)
        {
            errors["models"] = ["A round robin needs at least two models."];
        }
        else if (models.Any(m => string.IsNullOrWhiteSpace(m)))
        {
            errors["models"] = ["Every model needs an id."];
        }
        else if (models.Distinct().Count() != models.Count)
        {
            errors["models"] = ["Each model may appear only once."];
        }
        else if (models.Count > MaxModels)
        {
            errors["models"] = [$"A round robin may have at most {MaxModels} models."];
        }

        if (listing is not null && !errors.ContainsKey("models"))
        {
            var problems = models.Select(m => ModelVariant.Problem(m, listing)).OfType<string>().ToArray();
            if (problems.Length > 0)
            {
                errors["models"] = problems;
            }
        }

        // Colours alternate within a pair, so each pair needs an even number of games (at least one each way).
        if (request.GamesPerPair < 2 || request.GamesPerPair % 2 != 0)
        {
            errors["gamesPerPair"] = ["Games per pair must be an even number, at least 2, so that colours alternate."];
        }
        else if (request.GamesPerPair > MaxGamesPerPair)
        {
            errors["gamesPerPair"] = [$"Games per pair may be at most {MaxGamesPerPair}."];
        }
        else if (!errors.ContainsKey("models") && (long)models.Count * (models.Count - 1) / 2 * request.GamesPerPair > MaxGames)
        {
            errors["gamesPerPair"] = [$"A run may create at most {MaxGames} games; lower the games per pair or the number of models."];
        }

        if (request.MaxConcurrency < 1 || request.MaxConcurrency > maxConcurrentGames)
        {
            errors["maxConcurrency"] = [$"Max concurrency must be between 1 and {maxConcurrentGames} (Arena:MaxConcurrentGames)."];
        }

        return errors;
    }
}
