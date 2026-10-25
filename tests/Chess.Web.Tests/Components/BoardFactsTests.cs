using Chess.Web.Api;
using Chess.Web.Components.Shared;
using Chess.Web.Components.Shared.Game;
using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>What a move did to the board, read from FEN positions alone (the web project has no engine).</summary>
public class BoardFactsTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    [Fact]
    public void APawnMoveHighlightsItsFromAndToSquares()
    {
        Assert.Equal(new BoardMove("e2", "e4"), BoardFacts.LastMove(StartFen, AfterE4, "e4"));
    }

    [Fact]
    public void CastlingHighlightsTheKingsSquares()
    {
        const string before = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        const string after = "r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1";

        Assert.Equal(new BoardMove("e1", "g1"), BoardFacts.LastMove(before, after, "O-O"));
    }

    [Fact]
    public void ACaptureHighlightsTheCapturingPieceAndItsSquare()
    {
        const string before = "4k3/8/8/3p4/8/2N5/8/4K3 w - - 0 1";
        const string after = "4k3/8/8/3N4/8/8/8/4K3 b - - 0 1";

        Assert.Equal(new BoardMove("c3", "d5"), BoardFacts.LastMove(before, after, "Nxd5"));
    }

    [Fact]
    public void APromotionHighlightsThePawnsSquares()
    {
        const string before = "8/P7/8/8/8/8/8/K6k w - - 0 1";
        const string after = "Q7/8/8/8/8/8/8/K6k b - - 0 1";

        Assert.Equal(new BoardMove("a7", "a8"), BoardFacts.LastMove(before, after, "a8=Q"));
    }

    [Fact]
    public void TheKingInCheckIsTheKingOfTheSideToMove()
    {
        const string checkedFen = "3Qk3/8/8/8/8/8/8/6K1 b - - 1 1";

        Assert.Equal("e8", BoardFacts.CheckedKing(checkedFen, "Qd8+"));
        Assert.Equal("e8", BoardFacts.CheckedKing(checkedFen, "Qd8#"));
        Assert.Null(BoardFacts.CheckedKing(checkedFen, "Qd8"));
    }

    [Fact]
    public void MaterialShowsWhatEachSideTookAndTheLead()
    {
        const string afterD5 = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";
        const string afterExd5 = "rnbqkbnr/ppp1pppp/8/3P4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";

        var material = BoardFacts.MaterialOf(StartFen, [AfterE4, afterD5, afterExd5]);

        // White took the black pawn (drawn as a black glyph); nothing was taken from White.
        Assert.Equal("♟", material.CapturedByWhite);
        Assert.Equal(string.Empty, material.CapturedByBlack);
        Assert.Equal(1, material.Lead);
    }

    [Fact]
    public void AStartingPositionHasNoCapturesAndNoLead()
    {
        var material = BoardFacts.MaterialOf(StartFen, []);

        Assert.Equal(new Material(string.Empty, string.Empty, 0), material);
    }

    [Fact]
    public void TotalsAddUpOnlyTheAttemptsOfOneSide()
    {
        var entries = new[]
        {
            Entry(1, 1, 100, 10, 0.5),
            Entry(1, 2, 200, 20, null),
            Entry(2, 1, 300, 30, 1.25),
        };

        Assert.Equal(new SideTotals(2, 300, 30, 0.5), BoardFacts.Totals(entries, "White"));
        Assert.Equal(new SideTotals(1, 300, 30, 1.25), BoardFacts.Totals(entries, "Black"));
        Assert.Equal(new SideTotals(0, 0, 0, null), BoardFacts.Totals([], "White"));
    }

    private static EngineEntry Entry(int ply, int attempt, int input, int output, double? credits) =>
        new(ply, attempt, "gpt", "prompt", "e4", null, "e4", true, null, 900, input, output, null, credits, null);
}

public class GameFiguresTests
{
    [Fact]
    public void CountsAreGroupedAndAnUnknownCountIsADash()
    {
        Assert.Equal("1,234", GameFigures.Tokens(1234));
        Assert.Equal("—", GameFigures.Tokens(null));
    }

    [Fact]
    public void DollarsHaveTwoDecimalsAndSmallAmountsFour()
    {
        Assert.Equal("$0.12", GameFigures.Usd(0.1234));
        Assert.Equal("$0.0010", GameFigures.Usd(0.001));
        Assert.Equal("—", GameFigures.Usd(null));
    }

    [Fact]
    public void CreditsAndTheRateAreLabelled()
    {
        Assert.Equal("1.2346 cr", GameFigures.Credits(1.23456789));
        Assert.Equal("$0.0100 per AI credit", GameFigures.Rate(0.01));
        Assert.Equal("1.2 s", GameFigures.Latency(1234));
    }

    [Fact]
    public void APriceIsPerMillionTokensAndAModelWithNoPriceHasNone()
    {
        Assert.Equal("$3.00 / $15.00 per 1M tokens", GameFigures.Price(new ModelPricingDto(3, 15, 0.3)));
        Assert.Equal("— / $15.00 per 1M tokens", GameFigures.Price(new ModelPricingDto(null, 15, null)));
        Assert.Null(GameFigures.Price(new ModelPricingDto(null, null, 0.3)));
        Assert.Null(GameFigures.Price(null));
    }
}
