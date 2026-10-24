namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>
/// The rules of <c>POST /api/tournaments</c> (<c>TournamentRules</c> in Chess.Api), checked in the form so that the user
/// sees a problem before anything is sent. The API still checks every request.
/// </summary>
public static class TournamentFormRules
{
    /// <summary>The highest concurrency the form offers: the API's <c>Arena:MaxConcurrentGames</c> defaults to it.</summary>
    public const int MaxConcurrencyLimit = 2;

    /// <returns>The problems with the form, in the order they appear on it; empty when the form can be sent.</returns>
    public static IReadOnlyList<string> Problems(string name, IReadOnlyList<string> models, int gamesPerPair, int maxConcurrency)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("Give the tournament a name.");
        }

        if (models.Count < 2)
        {
            problems.Add("Choose at least two models.");
        }

        // Colours alternate within a pair, so each pair needs an even number of games, at least one each way.
        if (gamesPerPair < 2 || gamesPerPair % 2 != 0)
        {
            problems.Add("Games per pair must be an even number, at least 2, so that colours alternate.");
        }

        if (maxConcurrency < 1 || maxConcurrency > MaxConcurrencyLimit)
        {
            problems.Add($"Max concurrency must be between 1 and {MaxConcurrencyLimit}.");
        }

        return problems;
    }
}
