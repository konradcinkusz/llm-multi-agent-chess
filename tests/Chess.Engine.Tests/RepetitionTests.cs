using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

// FIDE 9.2: two positions are the same when the same pieces stand on the same squares, the same side is to move
// and the same moves are possible (castling rights and a legal en passant capture included).
public class RepetitionTests
{
    private static ChessBoard Load(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }

    private static void Play(ChessBoard board, params string[] sans)
    {
        foreach (var san in sans)
            Assert.True(board.ApplyMoveSan(san), $"{san} should be legal in {board.ToFen()}");
    }

    // After 1.e4 e5 the en passant square e6 is set but no white pawn can capture there, so the position
    // after 1...e5 is the same position every time the knights are home again: three times after 5...Nb8.
    [Fact]
    public void AnEnPassantSquareNobodyCanUseDoesNotMakeTheRepeatedPositionDifferent()
    {
        var board = ChessBoard.StartingPosition();

        Play(board, "e4", "e5", "Nf3", "Nc6", "Ng1", "Nb8");
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        Play(board, "Nf3", "Nc6", "Ng1");
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        Play(board, "Nb8");
        Assert.Equal(GameStatus.DrawRepetition, board.GetStatus());
    }

    // White's d5 pawn can take e6 en passant after ...e5, so that position (with the right to capture) is not
    // the one that returns after the knight and king shuffle: only the shuffled positions repeat.
    [Fact]
    public void AnEnPassantCaptureThatIsLegalMakesThePositionDifferentFromTheSameSquaresWithoutIt()
    {
        var board = Load("4k3/4p3/8/3P4/8/8/8/4K1N1 b - - 0 1");
        Play(board, "e5");
        Assert.Equal("e6", board.ToFen().Split(' ')[3]);

        // The shuffle returns the squares of the position after ...e5, now without the capture right: 2nd time.
        Play(board, "Nf3", "Kd8", "Ng1", "Ke8");
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        // 3rd time for the squares, but only the 2nd for the position without the right: still not a draw.
        Play(board, "Nf3", "Kd8", "Ng1", "Ke8");
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        // 3rd time for the position without the right.
        Play(board, "Nf3", "Kd8", "Ng1", "Ke8");
        Assert.Equal(GameStatus.DrawRepetition, board.GetStatus());
    }

    // The pawn on d5 is pinned along the fifth rank: capturing en passant would remove d5 and e5 and expose the
    // king on a5 to the rook on h5. No capture is possible, so the first position after ...e5 counts.
    [Fact]
    public void AnEnPassantCaptureThatWouldExposeTheKingDoesNotMakeThePositionDifferent()
    {
        var board = Load("4k3/4p3/8/K2P3r/8/8/8/6N1 b - - 0 1");

        Play(board, "e5", "Nf3", "Kd8", "Ng1", "Ke8");
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        Play(board, "Nf3", "Kd8", "Ng1", "Ke8");
        Assert.Equal(GameStatus.DrawRepetition, board.GetStatus());
    }

    [Fact]
    public void LosingACastlingRightMakesThePositionDifferent()
    {
        // The rook shuffle gives up the right to castle, so the squares repeat but the position does not.
        var board = Load("r3k3/8/8/8/8/8/8/R3K3 w Qq - 0 1");

        Play(board, "Rb1", "Rb8", "Ra1", "Ra8");
        Play(board, "Rb1", "Rb8", "Ra1", "Ra8");

        Assert.Equal(GameStatus.Ongoing, board.GetStatus());
    }

    [Fact]
    public void FenWithAnEnPassantSquareRoundTripsEvenWhenNoCaptureIsPossible()
    {
        var board = ChessBoard.StartingPosition();
        Play(board, "e4");

        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", board.ToFen());
    }
}
