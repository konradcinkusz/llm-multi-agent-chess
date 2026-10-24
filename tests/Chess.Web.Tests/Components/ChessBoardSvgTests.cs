using Bunit;
using Chess.Web.Components.Shared;
using Xunit;

namespace Chess.Web.Tests.Components;

public class ChessBoardSvgTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void StartPositionRendersThirtyTwoPieces()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen));

        Assert.Equal(32, cut.FindAll("[data-piece]").Count);
        Assert.Equal(64, cut.FindAll("[data-square]").Count);
    }

    [Fact]
    public void PiecesAreDrawnOnTheirSquares()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen));

        // e1 is column 4 of rank row 7: x = 4 * 100 + 50, y = 7 * 100 + 50.
        var whiteKing = cut.Find("[data-piece='K']");
        Assert.Equal("text", whiteKing.LocalName);
        Assert.Equal("♔", whiteKing.TextContent);
        Assert.Equal(("450", "750"), (whiteKing.GetAttribute("x"), whiteKing.GetAttribute("y")));

        // d8 is column 3 of row 0; black queen.
        var blackQueen = cut.Find("[data-piece='q']");
        Assert.Equal("♛", blackQueen.TextContent);
        Assert.Equal(("350", "50"), (blackQueen.GetAttribute("x"), blackQueen.GetAttribute("y")));
    }

    [Fact]
    public void HighlightsExactlyTheSquaresOfTheLastMove()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ChessBoardSvg>(p => p
            .Add(b => b.Fen, StartFen)
            .Add(b => b.LastMove, new BoardMove("e2", "e4")));

        var highlighted = cut.FindAll("[data-highlight]").Select(e => e.GetAttribute("data-highlight")!).Order().ToArray();
        Assert.Equal(new[] { "e2", "e4" }, highlighted);
    }

    [Fact]
    public void NoHighlightWithoutALastMove()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen));

        Assert.Empty(cut.FindAll("[data-highlight]"));
    }

    [Fact]
    public void UnflippedBoardStartsAtA8AndFlippedBoardStartsAtH1()
    {
        using var ctx = new BunitContext();

        var normal = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen));
        var flipped = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen).Add(b => b.Flipped, true));

        Assert.Equal("a8", normal.FindAll("[data-square]")[0].GetAttribute("data-square"));
        Assert.Equal("h1", flipped.FindAll("[data-square]")[0].GetAttribute("data-square"));
    }

    [Fact]
    public void FlippedBoardStillPlacesPiecesOnTheirSquares()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ChessBoardSvg>(p => p.Add(b => b.Fen, StartFen).Add(b => b.Flipped, true));

        // With Black at the bottom, the white king on e1 sits in column 3 (h→a order), row 0 (rank 1 at top).
        var whiteKing = cut.Find("[data-piece='K']");
        Assert.Equal(("350", "50"), (whiteKing.GetAttribute("x"), whiteKing.GetAttribute("y")));
    }
}

public class FenBoardTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void ParseMapsEverySquareOfTheStartPosition()
    {
        var pieces = FenBoard.Parse(StartFen);

        Assert.Equal(32, pieces.Count);
        Assert.Equal('K', pieces["e1"]);
        Assert.Equal('q', pieces["d8"]);
        Assert.False(pieces.ContainsKey("e4"));
    }

    [Theory]
    [InlineData("8/8/8/8/8/8/8 w")]          // seven ranks
    [InlineData("8/8/8/8/8/8/8/9 w")]        // a digit above eight
    [InlineData("7x/8/8/8/8/8/8/8 w")]       // an unknown piece letter
    [InlineData("4K4/8/8/8/8/8/8/8 w")]      // a rank that does not span eight files
    public void ParseRejectsMalformedPlacement(string fen)
    {
        Assert.Throws<FormatException>(() => FenBoard.Parse(fen));
    }

    [Fact]
    public void SymbolsUseWhiteOutlinesForUpperCaseAndBlackForLowerCase()
    {
        Assert.Equal("♔", FenBoard.Symbol('K'));
        Assert.Equal("♟", FenBoard.Symbol('p'));
    }

    [Fact]
    public void SquareNamesRunFromA1ToH8()
    {
        Assert.Equal("a1", FenBoard.Square(0, 0));
        Assert.Equal("h8", FenBoard.Square(7, 7));
    }
}
