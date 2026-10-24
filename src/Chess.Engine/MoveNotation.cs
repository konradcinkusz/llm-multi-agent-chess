using System.Text.RegularExpressions;

namespace Chess.Engine;

// Tolerant reading of a move written by a player or an LLM. Accepts exact SAN (check and annotation
// marks optional), castling as 0-0 / O-O-O, UCI (e2e4, e7e8q) and a leading move number. Resolves to
// the canonical SAN of the one legal move the text names; ambiguous or illegal text is rejected.
public static class MoveNotation
{
    private static readonly Regex LeadingMoveNumber = new(@"^\d+\s*\.+\s*");
    private static readonly Regex Uci = new(@"^[a-h][1-8][a-h][1-8][qrbn]?$");
    private static readonly char[] Wrapping = { '"', '\'', '`', ' ', '\t', '\r', '\n' };
    private static readonly char[] Trailing = { '.', ',', ';', ':', '!', '?', '+', '#', '"', '\'', '`', ' ', '\t', '\r', '\n' };

    public static bool TryResolve(ChessBoard board, string text, out string san)
    {
        san = string.Empty;
        var candidate = Clean(text);
        if (candidate.Length == 0)
            return false;

        var legal = board.GetLegalMoves();
        var matches = Uci.IsMatch(candidate)
            ? legal.Where(m => MatchesUci(m, candidate)).ToList()
            : legal.Where(m => WithoutCheckMark(m.San) == candidate).ToList();
        if (matches.Count != 1)
            return false;

        san = matches[0].San;
        return true;
    }

    public static string ToUci(ChessBoard board, string text)
    {
        if (!TryResolve(board, text, out var san))
            throw new ArgumentException($"'{text}' is not a legal move in this position.", nameof(text));

        var move = board.GetLegalMoves().First(m => m.San == san);
        return $"{ChessBoard.ToAlgebraic(move.From)}{ChessBoard.ToAlgebraic(move.To)}{PromotionLetter(move.Promotion)}";
    }

    private static string Clean(string text)
    {
        var s = LeadingMoveNumber.Replace(text.Trim(Wrapping), string.Empty).TrimEnd(Trailing);
        return s.StartsWith("0-0", StringComparison.Ordinal) ? s.Replace('0', 'O') : s;
    }

    private static bool MatchesUci(ChessMove move, string uci)
    {
        var promotion = uci.Length == 5 ? PromotionPiece(uci[4]) : PieceType.None;
        return move.From == ChessBoard.FromAlgebraic(uci[..2])
            && move.To == ChessBoard.FromAlgebraic(uci[2..4])
            && move.Promotion == promotion;
    }

    private static string WithoutCheckMark(string san) => san.TrimEnd('+', '#');

    private static PieceType PromotionPiece(char letter) => letter switch
    {
        'q' => PieceType.Queen, 'r' => PieceType.Rook, 'b' => PieceType.Bishop, 'n' => PieceType.Knight,
        _ => PieceType.None,
    };

    private static string PromotionLetter(PieceType piece) => piece switch
    {
        PieceType.Queen => "q", PieceType.Rook => "r", PieceType.Bishop => "b", PieceType.Knight => "n",
        _ => string.Empty,
    };
}
