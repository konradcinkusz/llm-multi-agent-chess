namespace Chess.Web.Site.Game;

/// <summary>What the game pages of the export contain (03 §11). Pure: no I/O.</summary>
public static class SiteGameRules
{
    /// <summary>The PGN file of a game, written next to its page.</summary>
    public const string PgnFileName = "game.pgn";

    /// <summary>
    /// Only the newest <paramref name="fullLogGames"/> games keep their engine log, to bound the size of the export.
    /// <paramref name="newestFirstIndex"/> is 0 for the newest finished game.
    /// </summary>
    public static bool KeepsEngineLog(int newestFirstIndex, int fullLogGames) => newestFirstIndex < fullLogGames;
}
