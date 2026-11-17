using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class FenValidationTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1", "8 ranks")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "8 ranks")]
    [InlineData("rnbqkbn/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "rank 8 has 7 squares")]
    [InlineData("rnbqkbnrr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "more than 8 squares")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "unknown character")]
    [InlineData("rnbqkbnr/pppppppp/44/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "two digits in a row")]
    [InlineData("rnbqkbnx/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "unknown character 'x'")]
    [InlineData("rnbq1bnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "exactly one king")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBKR w KQkq - 0 1", "exactly one king")]
    [InlineData("P3k3/8/8/8/8/8/8/4K3 w - - 0 1", "pawn on the first or last rank")]
    [InlineData("4k3/8/8/8/8/8/8/4K2p w - - 0 1", "pawn on the first or last rank")]
    [InlineData("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1", "king in check")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 x - - 0 1", "'w' or 'b'")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 white - - 0 1", "'w' or 'b'")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KX - 0 1", "castling")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KK - 0 1", "repeats")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w -- - 0 1", "castling")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - e4 0 1", "3rd or 6th rank")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - z6 0 1", "3rd or 6th rank")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - e3 0 1", "wrong rank")]
    [InlineData("4k3/8/8/8/8/4N3/8/4K3 b - e3 0 1", "not empty")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - abc 1", "halfmove clock")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - -1 1", "halfmove clock")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 0", "move number")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 x", "move number")]
    [InlineData("4k3/8/8/8/8/8/8/4K3", "4 to 6")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w -", "4 to 6")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1 extra", "4 to 6")]
    public void LoadFenRejectsAnInvalidPositionAndSaysWhy(string fen, string expectedInMessage)
    {
        var board = ChessBoard.StartingPosition();

        var ex = Assert.Throws<FormatException>(() => board.LoadFen(fen));

        Assert.Contains(expectedInMessage, ex.Message);
    }

    [Fact]
    public void ARejectedFenLeavesTheBoardAsItWas()
    {
        var board = ChessBoard.StartingPosition();
        Assert.True(board.ApplyMoveSan("e4"));
        var before = board.ToFen();

        Assert.False(board.TryLoadFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN w KQkq - 0 1", out var error));

        Assert.NotNull(error);
        Assert.Equal(before, board.ToFen());
        Assert.True(board.ApplyMoveSan("e5"));
    }

    [Fact]
    public void TryLoadFenAcceptsAValidPosition()
    {
        var board = new ChessBoard();

        Assert.True(board.TryLoadFen(Start, out var error));

        Assert.Null(error);
        Assert.Equal(Start, board.ToFen());
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - -", "4k3/8/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 7", "4k3/8/8/8/8/8/8/4K3 w - - 7 1")]
    [InlineData("  4k3/8/8/8/8/8/8/4K3   b  -  -  12  30 ", "4k3/8/8/8/8/8/8/4K3 b - - 12 30")]
    public void MissingClocksDefaultAndExtraSpacesAreTolerated(string fen, string canonical)
    {
        var board = new ChessBoard();

        board.LoadFen(fen);

        Assert.Equal(canonical, board.ToFen());
    }

    [Fact]
    public void ALegalEnPassantSquareIsAccepted()
    {
        var board = new ChessBoard();

        board.LoadFen("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");

        Assert.Contains(board.GetLegalMoves(), m => m.San == "exf6");
    }
}
