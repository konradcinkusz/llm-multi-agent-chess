using Chess.Web.Api;

namespace Chess.Web.Site;

/// <summary>
/// The price of every matchup of the menu, for the public menu page (03 §15.6): menu × menu, once per style of
/// <see cref="Styles"/>. Each price is the quote the API gave, so the page cannot disagree with the intake.
/// </summary>
public sealed record SitePriceMenu(RequestMenuDto Menu, IReadOnlyList<SitePriceCell> Cells)
{
    /// <summary>The styles the matrix shows, in the order of the page: classic first, then think (03 §15.2).</summary>
    public static readonly string[] Styles = ["classic", "think"];

    /// <summary>The cell for a white and a black menu code in one style, or null when the matrix has none.</summary>
    public SitePriceCell? Cell(string style, string white, string black) =>
        Cells.FirstOrDefault(cell => cell.Style == style && cell.White == white && cell.Black == black);
}

/// <summary>One matchup in one style. PriceUsd is null when the API does not offer the matchup (above the largest tier).</summary>
public sealed record SitePriceCell(string Style, string White, string Black, double? PriceUsd);
