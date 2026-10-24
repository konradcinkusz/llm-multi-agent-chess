using System.Text.RegularExpressions;
using Chess.Web.Components.Shared;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>
/// Works out the from and to squares of the move just played, for the board's last-move highlight. The server's
/// move list carries only SAN, so the squares come from the positions before and after the move.
/// </summary>
public static class LiveBoardHighlight
{
    private static readonly Regex SquareName = new("[a-h][1-8]", RegexOptions.CultureInvariant);

    /// <summary>The highlight for <paramref name="san"/>, or null when the squares cannot be told apart.</summary>
    /// <param name="before">The FEN before the move; its active colour is the side that moved.</param>
    /// <param name="after">The FEN after the move.</param>
    /// <param name="san">The move in standard algebraic notation, e.g. "exd5", "O-O" or "e8=Q+".</param>
    public static BoardMove? Of(string before, string after, string san)
    {
        var whiteMoved = before.Split(' ')[1] == "w";
        var rank = whiteMoved ? "1" : "8";

        if (san.StartsWith("O-O-O", StringComparison.Ordinal))
        {
            return new BoardMove($"e{rank}", $"c{rank}");
        }

        if (san.StartsWith("O-O", StringComparison.Ordinal))
        {
            return new BoardMove($"e{rank}", $"g{rank}");
        }

        var matches = SquareName.Matches(san);
        if (matches.Count == 0)
        {
            return null;
        }

        var to = matches[^1].Value;
        var pieces = FenBoard.Parse(before);
        var afterPieces = FenBoard.Parse(after);

        // The mover's piece left its square. Captured pieces belong to the other side, so they are not candidates.
        var from = pieces
            .Where(square => IsMover(square.Value, whiteMoved) && !afterPieces.ContainsKey(square.Key))
            .Select(square => square.Key)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        return from is null ? null : new BoardMove(from, to);
    }

    private static bool IsMover(char piece, bool whiteMoved) => char.IsUpper(piece) == whiteMoved;
}
