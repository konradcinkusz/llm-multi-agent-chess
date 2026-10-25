using System.Globalization;

namespace Chess.Web.Site.Game;

/// <summary>
/// The ply links of a game page (03 §11). Every board is the anchor <c>#ply-n</c>; its First, Previous, Next and Last links
/// point at the others, so the page needs no script. Ply 0 is the start position. Pure: no I/O.
/// </summary>
/// <param name="Ply">The ply of the board the links sit under.</param>
/// <param name="MoveCount">The number of moves in the game; the last ply.</param>
public sealed record SitePlies(int Ply, int MoveCount)
{
    /// <summary>The id of the board of <paramref name="ply"/>, which is also its anchor.</summary>
    public static string Anchor(int ply) => "ply-" + ply.ToString(CultureInfo.InvariantCulture);

    /// <summary>The link to the board of <paramref name="ply"/>.</summary>
    public static string Href(int ply) => "#" + Anchor(ply);

    /// <summary>The ply of the First link, or null on the start position, where the link is shown disabled.</summary>
    public int? First => Ply > 0 ? 0 : null;

    public int? Previous => Ply > 0 ? Ply - 1 : null;

    public int? Next => Ply < MoveCount ? Ply + 1 : null;

    public int? Last => Ply < MoveCount ? MoveCount : null;
}
