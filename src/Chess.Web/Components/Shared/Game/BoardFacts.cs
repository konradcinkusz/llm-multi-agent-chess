using System.Text;
using System.Text.RegularExpressions;
using Chess.Web.Components.Shared;

namespace Chess.Web.Components.Shared.Game;

/// <summary>
/// What a move did to the board, read from two FEN positions and the move's SAN. The web project references no engine, so
/// these are read from the placement alone. Pure.
/// </summary>
public static partial class BoardFacts
{
    private const string Pieces = "PNBRQK";

    /// <summary>The squares a move went from and to (castling: the king's), or null when the positions do not show it.</summary>
    public static BoardMove? LastMove(string previousFen, string fen, string san)
    {
        var whiteMoved = SideToMove(previousFen) == 'w';
        var piece = MovingPiece(san, whiteMoved);

        if (san.StartsWith("O-O", StringComparison.Ordinal))
        {
            var rank = whiteMoved ? "1" : "8";
            return new BoardMove("e" + rank, (san == "O-O" ? "g" : "c") + rank);
        }

        var destination = DestinationSquare().Matches(san) is { Count: > 0 } matches ? matches[^1].Value : null;
        var before = FenBoard.Parse(previousFen);
        var after = FenBoard.Parse(fen);

        // The mover's piece left one square: it is the one of that kind that is gone from its square.
        var origin = before
            .Where(square => square.Value == piece && !(after.TryGetValue(square.Key, out var now) && now == piece))
            .Select(square => square.Key)
            .FirstOrDefault();

        return origin is null || destination is null ? null : new BoardMove(origin, destination);
    }

    /// <summary>The king of the side to move when the move just played gave check (SAN '+' or '#'); otherwise null.</summary>
    public static string? CheckedKing(string fen, string san)
    {
        if (!san.EndsWith('+') && !san.EndsWith('#'))
        {
            return null;
        }

        var king = SideToMove(fen) == 'w' ? 'K' : 'k';
        return FenBoard.Parse(fen).Where(square => square.Value == king).Select(square => square.Key).FirstOrDefault();
    }

    /// <summary>
    /// The pieces each side has taken, and the material lead. Each move's captures are the opponent's pieces that changed
    /// square between the two positions, so promotions do not count as captures.
    /// </summary>
    public static Material MaterialOf(string startFen, IEnumerable<string> positionsAfterEachMove)
    {
        var capturedByWhite = new List<char>();
        var capturedByBlack = new List<char>();
        var previous = startFen;

        foreach (var fen in positionsAfterEachMove)
        {
            var whiteMoved = SideToMove(previous) == 'w';
            var before = FenBoard.Parse(previous);
            var after = FenBoard.Parse(fen);
            foreach (var (square, piece) in before)
            {
                var isOpponent = char.IsUpper(piece) != whiteMoved;
                if (!isOpponent || (after.TryGetValue(square, out var now) && now == piece))
                {
                    continue;
                }

                (whiteMoved ? capturedByWhite : capturedByBlack).Add(char.ToUpperInvariant(piece));
            }

            previous = fen;
        }

        return new Material(Symbols(capturedByWhite, black: true), Symbols(capturedByBlack, black: false),
            Points(capturedByWhite) - Points(capturedByBlack));
    }

    /// <summary>What a side has spent so far, from its attempts: requests, tokens and AI credits (null when none was reported).</summary>
    public static SideTotals Totals(IEnumerable<EngineEntry> entries, string color)
    {
        var mine = entries.Where(entry => entry.Color == color).ToArray();
        var credits = mine.Where(entry => entry.AiCredits is not null).Select(entry => entry.AiCredits!.Value).ToArray();
        return new SideTotals(
            mine.Length,
            mine.Sum(entry => (long)(entry.InputTokens ?? 0)),
            mine.Sum(entry => (long)(entry.OutputTokens ?? 0)),
            credits.Length == 0 ? null : credits.Sum());
    }

    private static char SideToMove(string fen) => fen.Split(' ')[1][0];

    private static char MovingPiece(string san, bool white)
    {
        var letter = san.StartsWith("O-O", StringComparison.Ordinal) ? 'K'
            : Pieces.Contains(san[0]) ? san[0]
            : 'P';
        return white ? letter : char.ToLowerInvariant(letter);
    }

    private static string Symbols(IEnumerable<char> pieces, bool black)
    {
        var builder = new StringBuilder();
        foreach (var piece in pieces.OrderByDescending(Value))
        {
            builder.Append(FenBoard.Symbol(black ? char.ToLowerInvariant(piece) : piece));
        }

        return builder.ToString();
    }

    private static int Points(IEnumerable<char> pieces) => pieces.Sum(Value);

    private static int Value(char piece) => char.ToUpperInvariant(piece) switch
    {
        'P' => 1,
        'N' or 'B' => 3,
        'R' => 5,
        'Q' => 9,
        _ => 0,
    };

    [GeneratedRegex("[a-h][1-8]")]
    private static partial Regex DestinationSquare();
}

/// <summary>
/// The captured pieces of each side, as glyphs, and the lead in points: positive when White is ahead. Glyphs are listed
/// by value, largest first, as chess sites show them.
/// </summary>
public sealed record Material(string CapturedByWhite, string CapturedByBlack, int Lead);

/// <summary>What one side has spent: requests, input and output tokens, and AI credits (null when no attempt reported any).</summary>
public sealed record SideTotals(int Requests, long InputTokens, long OutputTokens, double? AiCredits);
