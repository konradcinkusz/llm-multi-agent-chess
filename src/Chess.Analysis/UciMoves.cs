using Chess.Engine;

namespace Chess.Analysis;

/// <summary>UCI spelling of a legal move. Temporary: replaced by Chess.Engine's MoveNotation.ToUci (T1.10).</summary>
internal static class UciMoves
{
    public static string ToUci(ChessMove move)
    {
        var promotion = move.Promotion switch
        {
            PieceType.Queen => "q",
            PieceType.Rook => "r",
            PieceType.Bishop => "b",
            PieceType.Knight => "n",
            _ => ""
        };
        return ChessBoard.ToAlgebraic(move.From) + ChessBoard.ToAlgebraic(move.To) + promotion;
    }
}
