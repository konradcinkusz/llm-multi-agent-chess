using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class MoveNotationTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const string Castling = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
    private const string Promotion = "8/P7/3k4/8/8/8/8/7K w - - 0 1";
    private const string ScholarCheck = "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4";
    private const string AmbiguousKnights = "4k3/8/8/8/8/2N1N3/8/4K3 w - - 0 1";

    [Theory]
    // exact SAN, check/annotation marks, punctuation, quotes
    [InlineData(Start, "e4", "e4")]
    [InlineData(Start, "Nf3", "Nf3")]
    [InlineData(Start, "Nf3+", "Nf3")]
    [InlineData(Start, "d4", "d4")]
    [InlineData(Start, "e4!?", "e4")]
    [InlineData(Start, "Nf3.", "Nf3")]
    [InlineData(Start, "\"e4\"", "e4")]
    [InlineData(Start, "`Nf3`", "Nf3")]
    // leading move numbers
    [InlineData(Start, "1. e4", "e4")]
    [InlineData(Start, "1.e4", "e4")]
    [InlineData(AfterE4, "12... Nf6", "Nf6")]
    [InlineData(AfterE4, "12...Nf6", "Nf6")]
    [InlineData(AfterE4, "1...c5", "c5")]
    // UCI
    [InlineData(Start, "e2e4", "e4")]
    [InlineData(Start, "g1f3", "Nf3")]
    [InlineData(Start, "b1c3", "Nc3")]
    // castling: zeros, letters, and UCI king moves
    [InlineData(Castling, "0-0", "O-O")]
    [InlineData(Castling, "0-0-0", "O-O-O")]
    [InlineData(Castling, "O-O-O", "O-O-O")]
    [InlineData(Castling, "e1g1", "O-O")]
    [InlineData(Castling, "e1c1", "O-O-O")]
    // promotion, by SAN and by UCI
    [InlineData(Promotion, "a8=Q", "a8=Q")]
    [InlineData(Promotion, "a7a8q", "a8=Q")]
    // the canonical SAN carries the check mark even when the text omits it
    [InlineData(ScholarCheck, "Qxf7", "Qxf7#")]
    [InlineData(ScholarCheck, "Qxf7+", "Qxf7#")]
    // negative: illegal for the side to move, or not a move at all
    [InlineData(Start, "Nd4", null)]
    [InlineData(Start, "e5", null)]
    [InlineData(Start, "Nf6", null)]
    [InlineData(Start, "O-O", null)]
    [InlineData(Start, "e2e5", null)]
    // negative: two moves in one reply
    [InlineData(Start, "Nf3 Nf6", null)]
    // negative: empty reply
    [InlineData(Start, "", null)]
    // negative: ambiguous, both knights reach d5 and SAN needs a file to tell them apart
    [InlineData(AmbiguousKnights, "Nd5", null)]
    // negative: promotion piece missing
    [InlineData(Promotion, "a7a8", null)]
    [InlineData(Promotion, "a8N", null)]
    public void TryResolveAcceptsTolerantTextAndRejectsTheRest(string fen, string text, string? expected)
    {
        var board = Load(fen);

        var resolved = MoveNotation.TryResolve(board, text, out var san);

        Assert.Equal(expected is not null, resolved);
        Assert.Equal(expected ?? string.Empty, san);
    }

    [Fact]
    public void ResolvedSanIsAcceptedByApplyMoveSan()
    {
        var board = Load(ScholarCheck);

        Assert.True(MoveNotation.TryResolve(board, "Qxf7+", out var san));
        Assert.True(board.ApplyMoveSan(san));
        Assert.Equal(GameStatus.Checkmate, board.GetStatus());
    }

    [Theory]
    [InlineData(Start, "e4", "e2e4")]
    [InlineData(Start, "Nf3", "g1f3")]
    [InlineData(Castling, "O-O", "e1g1")]
    [InlineData(Castling, "0-0-0", "e1c1")]
    [InlineData(Promotion, "a8=Q", "a7a8q")]
    [InlineData(ScholarCheck, "Qxf7+", "h5f7")]
    public void ToUciGivesTheLegalMoveInUciForm(string fen, string san, string uci)
    {
        Assert.Equal(uci, MoveNotation.ToUci(Load(fen), san));
    }

    [Fact]
    public void ToUciRejectsAnIllegalMove()
    {
        Assert.Throws<ArgumentException>(() => MoveNotation.ToUci(Load(Start), "Nd4"));
    }

    private static ChessBoard Load(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }
}
