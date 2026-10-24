using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Components.Shared;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class LiveBoardHighlightTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void APawnPushHighlightsItsTwoSquares()
    {
        var move = LiveBoardHighlight.Of(Start, "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "e4");

        Assert.Equal(new BoardMove("e2", "e4"), move);
    }

    [Fact]
    public void ABlackMoveIsFoundFromTheSideToMoveBefore()
    {
        var before = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        var after = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

        Assert.Equal(new BoardMove("e7", "e5"), LiveBoardHighlight.Of(before, after, "e5"));
    }

    [Fact]
    public void ACaptureHighlightsTheCapturersOriginAndTheCapturedSquare()
    {
        var before = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";
        var after = "rnbqkbnr/ppp1pppp/8/3P4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";

        Assert.Equal(new BoardMove("e4", "d5"), LiveBoardHighlight.Of(before, after, "exd5"));
    }

    [Fact]
    public void EnPassantHighlightsTheSquareTheCapturerLandsOn_NotTheCapturedPawn()
    {
        var before = "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3";
        var after = "rnbqkbnr/ppp1p1pp/5P2/3p4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 3";

        Assert.Equal(new BoardMove("e5", "f6"), LiveBoardHighlight.Of(before, after, "exf6"));
    }

    [Fact]
    public void WhiteKingsideCastlingHighlightsTheKingsMove()
    {
        var before = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        var after = "r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1";

        Assert.Equal(new BoardMove("e1", "g1"), LiveBoardHighlight.Of(before, after, "O-O"));
    }

    [Fact]
    public void BlackQueensideCastlingHighlightsTheKingsMoveOnTheEighthRank()
    {
        var before = "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1";
        var after = "2kr3r/8/8/8/8/8/8/R3K2R w KQk - 1 2";

        Assert.Equal(new BoardMove("e8", "c8"), LiveBoardHighlight.Of(before, after, "O-O-O"));
    }

    [Fact]
    public void APromotionHighlightsThePawnsSquareAndTheEighthRank()
    {
        var before = "8/P6k/8/8/8/8/8/K7 w - - 0 1";
        var after = "Q7/7k/8/8/8/8/8/K7 b - - 0 1";

        Assert.Equal(new BoardMove("a7", "a8"), LiveBoardHighlight.Of(before, after, "a8=Q"));
    }

    [Fact]
    public void ASanWithNoSquareGivesNoHighlight()
    {
        Assert.Null(LiveBoardHighlight.Of(Start, Start, "--"));
    }
}
