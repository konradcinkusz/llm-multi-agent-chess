using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class SanTests
{
    private static ChessBoard Load(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }

    private static List<string> SanOf(ChessBoard board) =>
        board.GetLegalMoves().Select(m => m.San).ToList();

    [Fact]
    public void StartingPositionSanIsStandard()
    {
        var sans = SanOf(ChessBoard.StartingPosition());

        Assert.Contains("e4", sans);
        Assert.Contains("Nf3", sans);
        Assert.Contains("Nc3", sans);
        Assert.Equal(20, sans.Distinct().Count());
    }

    [Fact]
    public void RooksOnSameRankUseFileToDisambiguate()
    {
        var board = Load("4k3/8/8/8/8/8/7K/R6R w - - 0 1");

        var sans = SanOf(board);

        Assert.Contains("Rad1", sans);
        Assert.Contains("Rhd1", sans);
    }

    [Fact]
    public void RooksOnSameFileUseRankToDisambiguate()
    {
        var board = Load("4k3/8/8/8/R7/8/8/R3K3 w - - 0 1");

        var sans = SanOf(board);

        Assert.Contains("R4a2", sans);
        Assert.Contains("R1a2", sans);
    }

    [Fact]
    public void QueensNeedFileAndRankWhenBothAmbiguityExists()
    {
        // Qd1 shares a file with Qd8 and a rank with Qh1; all three reach d5.
        var board = Load("k2Q4/8/8/8/8/7K/8/3Q3Q w - - 0 1");

        var sans = SanOf(board);

        Assert.Contains("Qd1d5+", sans);
        Assert.Contains("Q8d5+", sans);
        Assert.Contains("Qhd5+", sans);
    }

    [Fact]
    public void KingsideAndQueensideCastlingUseZeroNotation()
    {
        var board = Load("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        var sans = SanOf(board);

        Assert.Contains("O-O", sans);
        Assert.Contains("O-O-O", sans);
    }

    [Fact]
    public void CastlingIsRecordedWithoutPieceLetterAndMovesTheRook()
    {
        var board = Load("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        Assert.True(board.ApplyMoveSan("O-O"));

        Assert.Equal("r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1", board.ToFen());
    }

    [Fact]
    public void PromotionsListAllFourPiecesWithCheckSuffixOnlyWhereItChecks()
    {
        // Pawn on a7 promotes on a8. Queen and rook give check along rank 8; bishop and knight do not.
        var board = Load("6k1/P7/8/8/8/8/8/4K3 w - - 0 1");

        var sans = SanOf(board);

        Assert.Contains("a8=Q+", sans);
        Assert.Contains("a8=R+", sans);
        Assert.Contains("a8=B", sans);
        Assert.Contains("a8=N", sans);
    }

    [Fact]
    public void BackRankMateEndsWithMateSuffix()
    {
        var board = Load("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");

        Assert.Contains("Ra8#", SanOf(board));
    }

    [Fact]
    public void PawnCaptureNamesOriginFile()
    {
        var board = Load("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1");

        Assert.Contains("exd5", SanOf(board));
    }

    [Fact]
    public void EnPassantCaptureIsNamedAsOrdinaryPawnCapture()
    {
        var board = Load("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");

        Assert.Contains("exf6", SanOf(board));
        Assert.True(board.ApplyMoveSan("exf6"));
        Assert.Equal("rnbqkbnr/ppp1p1pp/5P2/3p4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 3", board.ToFen());
    }

    [Fact]
    public void CheckSuffixIsAppendedToNormalMoves()
    {
        var board = Load("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");

        Assert.Contains("Ra8+", SanOf(board));
    }

    [Fact]
    public void ApplyMoveSanRejectsIllegalAndUnknownText()
    {
        var board = ChessBoard.StartingPosition();

        Assert.False(board.ApplyMoveSan("e5"));
        Assert.False(board.ApplyMoveSan("Ke2"));
        Assert.False(board.ApplyMoveSan("garbage"));
        Assert.Equal(ChessBoard.StartingPosition().ToFen(), board.ToFen());
    }
}
