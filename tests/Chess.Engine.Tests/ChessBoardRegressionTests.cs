using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

// One test per engine bug fixed in T1.5. The bug-named tests fail on the parent commit;
// FindBySanReturnsTheLegalMoveForKnownSan guards the happy path of the same fix.
public class ChessBoardRegressionTests
{
    // FindBySan returned a default (0→0) move instead of null for unknown SAN, so
    // ApplyMoveSan "played" it and emptied a1.
    [Theory]
    [InlineData("e5")]
    [InlineData("Ke2")]
    [InlineData("garbage")]
    [InlineData("")]
    public void ApplyMoveSanRejectsUnknownOrIllegalTextAndLeavesBoardUntouched(string san)
    {
        var board = ChessBoard.StartingPosition();
        string before = board.ToFen();

        Assert.False(board.ApplyMoveSan(san));

        Assert.Equal(before, board.ToFen());
    }

    [Fact]
    public void FindBySanReturnsNullForUnknownSan()
    {
        Assert.Null(ChessBoard.StartingPosition().FindBySan("e5"));
    }

    [Fact]
    public void FindBySanReturnsTheLegalMoveForKnownSan()
    {
        var move = ChessBoard.StartingPosition().FindBySan("e4");

        Assert.NotNull(move);
        Assert.Equal(ChessBoard.FromAlgebraic("e2"), move.Value.From);
        Assert.Equal(ChessBoard.FromAlgebraic("e4"), move.Value.To);
    }

    // K+B v K+B with both bishops on one colour is a FIDE dead position; it was reported as Ongoing.
    [Fact]
    public void SameColourBishopsEachSideIsInsufficientMaterial()
    {
        var board = new ChessBoard();
        board.LoadFen("8/8/2b5/4k3/8/8/8/4KB2 w - - 0 1");

        Assert.Equal(GameStatus.DrawInsufficientMaterial, board.GetStatus());
    }

    // Checkmate was reported as a fifty-move draw when the 100th half-move delivered mate.
    [Fact]
    public void CheckmateOnTheHundredthHalfMoveIsCheckmateNotADraw()
    {
        var board = new ChessBoard();
        board.LoadFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 100 3");

        Assert.Equal(GameStatus.Checkmate, board.GetStatus());
    }
}
