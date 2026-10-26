using System.Text;
using Chess.Engine;

namespace Chess.Api.Players.Prompts;

/// <summary>
/// The position as text, shared by the board and think styles (03 §14.2). It gives the model the facts of the position and no
/// evaluation: nothing says what is attacked, hanging or best (no engine help). Pure: the same request always gives the same lines.
/// </summary>
public static class BoardText
{
    public const string Goal =
        "You are playing a game of chess against another language model. Play to win; checkmate ends the game. " +
        "Trade pieces only when the trade gains something. A draw by the fifty-move rule, by threefold repetition or by " +
        "insufficient material is worth half a point. In the endgame, activate your king and make a passed pawn.";

    private const string Files = "  a b c d e f g h";

    /// <summary>The position lines of a move prompt. A plan line follows the side to move when <paramref name="plan"/> is given.</summary>
    public static IReadOnlyList<string> Describe(MoveRequest request, string? plan)
    {
        var board = new ChessBoard();
        board.LoadFen(request.Fen);

        var lines = new List<string>
        {
            $"You are playing a game of chess. It is {(board.SideToMove == Side.White ? "White" : "Black")} to move.",
        };
        if (plan is not null)
        {
            lines.Add($"Your plan from your last move: {plan}");
        }

        lines.Add($"Position (FEN): {request.Fen}");
        lines.Add("Board (rank 8 at the top, White in upper case):");
        lines.AddRange(Diagram(board));
        lines.Add($"White: {PieceList(board, Side.White)}");
        lines.Add($"Black: {PieceList(board, Side.Black)}");
        lines.Add(Material(board));
        lines.Add($"Game (PGN moves): {RatedPromptBuilder.FormatMoves(request.Fen, request.HistorySan, int.MaxValue)}");
        lines.Add($"Legal moves: {string.Join(", ", request.LegalSan.Order(StringComparer.Ordinal))}");
        return lines;
    }

    /// <summary>Eight rank lines from rank 8 down, each square as its FEN letter or a dot, then the file letters.</summary>
    public static IEnumerable<string> Diagram(ChessBoard board)
    {
        for (var rank = 7; rank >= 0; rank--)
        {
            var row = new StringBuilder().Append(rank + 1);
            for (var file = 0; file < 8; file++)
            {
                var piece = board[ChessBoard.Square(file, rank)];
                row.Append(' ').Append(piece.IsEmpty ? '.' : piece.FenChar);
            }

            yield return row.ToString();
        }

        yield return Files;
    }

    /// <summary>One side's pieces in SAN letters, the king first and then queens, rooks, bishops and knights, each by square; then the pawns.</summary>
    public static string PieceList(ChessBoard board, Side side)
    {
        var parts = new List<string>();
        foreach (var (type, letter) in new[]
        {
            (PieceType.King, "K"), (PieceType.Queen, "Q"), (PieceType.Rook, "R"), (PieceType.Bishop, "B"), (PieceType.Knight, "N"),
        })
        {
            parts.AddRange(SquaresOf(board, side, type).Select(square => letter + ChessBoard.ToAlgebraic(square)));
        }

        var pawns = SquaresOf(board, side, PieceType.Pawn).Select(ChessBoard.ToAlgebraic).ToList();
        parts.Add(pawns.Count == 0 ? "no pawns" : "pawns " + string.Join(", ", pawns));
        return string.Join(", ", parts);
    }

    /// <summary>The material of each side in pawn points, and the balance.</summary>
    public static string Material(ChessBoard board)
    {
        var (white, black) = board.MaterialCounts();
        var difference = white - black;
        var balance = difference switch
        {
            0 => "level",
            > 0 => $"White +{difference}",
            _ => $"Black +{-difference}",
        };
        return $"Material: White {white}, Black {black} (pawn 1, knight and bishop 3, rook 5, queen 9). Balance: {balance}.";
    }

    private static IEnumerable<int> SquaresOf(ChessBoard board, Side side, PieceType type)
    {
        for (var square = 0; square < 64; square++)
        {
            var piece = board[square];
            if (piece.Type == type && piece.Color == side)
            {
                yield return square;
            }
        }
    }
}
