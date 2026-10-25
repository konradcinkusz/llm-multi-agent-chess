using Chess.Api.Catalog;
using Chess.Api.Contracts;

namespace Chess.Api.Tournaments;

/// <summary>The checks a tournament request must pass before any game is created. Keys are the JSON property names.</summary>
public static class TournamentRules
{
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

        if (request.MaxConcurrency < 1 || request.MaxConcurrency > maxConcurrentGames)
        {
            errors["maxConcurrency"] = [$"Max concurrency must be between 1 and {maxConcurrentGames} (Arena:MaxConcurrentGames)."];
        }

        return errors;
    }
}
