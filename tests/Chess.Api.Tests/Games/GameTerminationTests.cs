using Chess.Api.Games;
using Chess.Engine;
using Xunit;

namespace Chess.Api.Tests.Games;

public class GameTerminationTests
{
    [Fact]
    public void FoolsMateEndsWithBlackWinning()
    {
        var board = BoardFrom("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        var end = GameTermination.FromStatus(board);

        Assert.Equal(new GameEnd("0-1", "checkmate"), end);
    }

    [Fact]
    public void StalemateIsAHalfPointDraw()
    {
        var board = BoardFrom("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.Equal(new GameEnd("1/2-1/2", "stalemate"), GameTermination.FromStatus(board));
    }

    [Fact]
    public void KingAgainstKingIsInsufficientMaterial()
    {
        var board = BoardFrom("8/8/8/4k3/8/8/4K3/8 w - - 0 1");

        Assert.Equal(new GameEnd("1/2-1/2", "insufficient-material"), GameTermination.FromStatus(board));
    }

    [Fact]
    public void AHundredHalfMovesWithoutCaptureOrPawnMoveIsFiftyMove()
    {
        var board = BoardFrom("4k3/8/8/8/8/8/R7/4K3 w - - 100 80");

        Assert.Equal(new GameEnd("1/2-1/2", "fifty-move"), GameTermination.FromStatus(board));
    }

    [Fact]
    public void ThePositionThreeTimesIsThreefoldRepetition()
    {
        var board = BoardFrom(null);
        foreach (var san in new[] { "Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8" })
        {
            Assert.True(board.ApplyMoveSan(san));
        }

        Assert.Equal(new GameEnd("1/2-1/2", "threefold"), GameTermination.FromStatus(board));
    }

    [Fact]
    public void AnOrdinaryPositionHasNoEnd()
    {
        Assert.Null(GameTermination.FromStatus(BoardFrom(null)));
    }

    [Fact]
    public void AForfeitLosesForTheSideThatForfeited()
    {
        Assert.Equal(new GameEnd("0-1", "forfeit-white"), GameTermination.Forfeit(Side.White));
        Assert.Equal(new GameEnd("1-0", "forfeit-black"), GameTermination.Forfeit(Side.Black));
    }

    [Fact]
    public void AbortedGamesHaveNoResult()
    {
        Assert.Equal(new GameEnd("*", "aborted"), GameTermination.Aborted);
    }

    [Theory]
    [InlineData(300, "1-0")]
    [InlineData(1000, "1-0")]
    [InlineData(299, "1/2-1/2")]
    [InlineData(0, "1/2-1/2")]
    [InlineData(-299, "1/2-1/2")]
    [InlineData(-300, "0-1")]
    public void StockfishVerdictDecidesAtPlusMinusThreeHundredCentipawns(int whiteCentipawns, string result)
    {
        var board = BoardFrom(null);

        Assert.Equal(new GameEnd(result, "adjudicated"), GameTermination.Adjudicate(board, whiteCentipawns));
    }

    [Fact]
    public void WithoutStockfishFiveMaterialPointsDecide()
    {
        // White has a rook (5) and nothing else: the difference is exactly the threshold.
        var rookUp = BoardFrom("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");

        Assert.Equal(new GameEnd("1-0", "adjudicated"), GameTermination.Adjudicate(rookUp, null));
    }

    [Fact]
    public void WithoutStockfishFourMaterialPointsAreADraw()
    {
        // White has a bishop and a knight (6), black a bishop (3): the difference is 3.
        var nearlyEven = BoardFrom("4kb2/8/8/8/8/8/8/B1N1K3 w - - 0 1");

        Assert.Equal(new GameEnd("1/2-1/2", "adjudicated"), GameTermination.Adjudicate(nearlyEven, null));
    }

    [Fact]
    public void WithoutStockfishTheMaterialDifferenceCanGoToBlack()
    {
        var blackUp = BoardFrom("4k3/8/8/8/8/8/8/q3K3 w - - 0 1");

        Assert.Equal(new GameEnd("0-1", "adjudicated"), GameTermination.Adjudicate(blackUp, null));
    }

    private static ChessBoard BoardFrom(string? fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen ?? GameHarness.StartFen);
        return board;
    }
}
