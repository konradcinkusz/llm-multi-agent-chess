using Chess.Analysis;
using Chess.Engine;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class UciMovesTests
{
    [Fact]
    public void Every_legal_move_of_the_start_position_has_its_uci_spelling()
    {
        var board = ChessBoard.StartingPosition();

        var uci = board.GetLegalMoves().Select(UciMoves.ToUci).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.Equal(20, uci.Count);
        Assert.Contains("g1f3", uci);
        Assert.Contains("b1c3", uci);
        Assert.DoesNotContain("g1g3", uci);
    }

    [Fact]
    public void Castling_is_the_king_moving_two_squares()
    {
        var board = new ChessBoard();
        board.LoadFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        var castle = board.GetLegalMoves().Single(m => m.San == "O-O");

        Assert.Equal("e1g1", UciMoves.ToUci(castle));
    }

    [Theory]
    [InlineData("b7b8q", PieceType.Queen)]
    [InlineData("b7b8r", PieceType.Rook)]
    [InlineData("b7b8b", PieceType.Bishop)]
    [InlineData("b7b8n", PieceType.Knight)]
    public void Promotion_appends_the_lower_case_piece_letter(string expected, PieceType promotion)
    {
        var move = new ChessMove(ChessBoard.FromAlgebraic("b7"), ChessBoard.FromAlgebraic("b8"), promotion);

        Assert.Equal(expected, UciMoves.ToUci(move));
    }
}
