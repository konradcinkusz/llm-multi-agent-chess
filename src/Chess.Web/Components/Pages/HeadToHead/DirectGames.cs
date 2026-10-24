using Chess.Web.Api;

namespace Chess.Web.Components.Pages.HeadToHead;

/// <summary>Selects the games two models played against each other. Pure: no I/O, no clock.</summary>
public static class DirectGames
{
    /// <summary>The games in which <paramref name="a"/> and <paramref name="b"/> met, whichever side each had. Newest first.</summary>
    public static IReadOnlyList<GameSummaryDto> Between(IEnumerable<GameSummaryDto> games, string a, string b) =>
        games.Where(g => IsPair(g, a, b)).OrderByDescending(g => g.CreatedAt).ToArray();

    /// <summary>
    /// The score of <paramref name="model"/> in a finished game: 1, 0.5 or 0. Null while the game
    /// is not finished, so a running game does not count as a loss.
    /// </summary>
    public static double? ScoreFor(GameSummaryDto game, string model)
    {
        if (game.State != "finished")
        {
            return null;
        }

        var whiteScore = game.Result switch
        {
            "1-0" => 1.0,
            "0-1" => 0.0,
            "1/2-1/2" => 0.5,
            _ => (double?)null,
        };

        if (whiteScore is not { } white)
        {
            return null;
        }

        return game.White.ModelId == model ? white : 1 - white;
    }

    private static bool IsPair(GameSummaryDto game, string a, string b) =>
        (game.White.ModelId == a && game.Black.ModelId == b) || (game.White.ModelId == b && game.Black.ModelId == a);
}
