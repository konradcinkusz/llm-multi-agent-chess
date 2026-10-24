namespace Chess.Web.Components.Pages.Games;

/// <summary>
/// The PGN download link. The browser never reaches the API (03 §6), so the PGN text is carried in a
/// <c>data:</c> link that the browser saves under <see cref="FileName"/>.
/// </summary>
public static class PgnDownload
{
    public const string MediaType = "application/x-chess-pgn";

    /// <summary>The name the API gives the file in its own Content-Disposition.</summary>
    public static string FileName(Guid gameId) => $"game-{gameId}.pgn";

    public static string Href(string pgn) => $"data:{MediaType};charset=utf-8,{Uri.EscapeDataString(pgn)}";
}
