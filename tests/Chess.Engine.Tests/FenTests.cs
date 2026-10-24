using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class FenTests
{
    // Canonical FENs: ToFen(LoadFen(fen)) must return the same string.
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/1RN2Q2/Pp1P2PP/R3K2R w KQkq - 0 1")]
    [InlineData("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1")]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8")]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10")]
    [InlineData("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3")]
    [InlineData("4k2r/8/8/8/8/8/8/4K3 b k - 0 1")]
    [InlineData("r3k3/8/8/8/8/8/8/R3K3 w Q - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("8/8/8/8/8/8/6k1/4K2R w K - 0 1")]
    [InlineData("3k4/8/8/8/8/8/8/3K3B w - - 49 75")]
    [InlineData("n3k3/8/8/8/8/8/8/4K2N w - - 0 1")]
    [InlineData("8/8/8/4k3/8/8/8/4K1B1 w - - 0 1")]
    [InlineData("1k6/8/8/8/8/8/8/1K6 w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1")]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1")]
    [InlineData("4q2k/8/8/8/8/8/8/4K3 w - - 37 60")]
    [InlineData("k7/8/8/8/8/8/8/K6R b - - 0 1")]
    [InlineData("8/8/8/8/8/2k5/8/K1Q5 b - - 12 40")]
    public void ToFenReproducesLoadedFen(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);

        Assert.Equal(fen, board.ToFen());
    }

    [Fact]
    public void StartingPositionSerialisesToStandardFen()
    {
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            ChessBoard.StartingPosition().ToFen());
    }

    [Fact]
    public void FenWithoutCastlingRightsWritesDash()
    {
        var board = new ChessBoard();
        board.LoadFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");

        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", board.ToFen());
    }

    [Fact]
    public void DoublePawnPushRecordsEnPassantSquareInFen()
    {
        var board = ChessBoard.StartingPosition();

        Assert.True(board.ApplyMoveSan("e4"));

        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", board.ToFen());
    }

    [Fact]
    public void MoveCountersAdvanceAfterBlackMove()
    {
        var board = ChessBoard.StartingPosition();

        board.ApplyMoveSan("Nf3");
        board.ApplyMoveSan("Nf6");

        Assert.Equal("rnbqkb1r/pppppppp/5n2/8/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 2 2", board.ToFen());
        Assert.Equal(2, board.FullMoveNumber);
        Assert.Equal(2, board.HalfMoveClock);
    }
}
