using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class StatusTests
{
    private static ChessBoard Load(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }

    [Fact]
    public void FoolsMateIsCheckmate()
    {
        var board = Load("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        Assert.Equal(GameStatus.Checkmate, board.GetStatus());
        Assert.True(board.IsGameOver());
        Assert.Equal("Checkmate! Black wins!", board.GetStatusText());
    }

    [Fact]
    public void BlackCheckmatedWhiteWinsText()
    {
        var board = Load("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
        board.ApplyMoveSan("Ra8#");

        Assert.Equal(GameStatus.Checkmate, board.GetStatus());
        Assert.Equal("Checkmate! White wins!", board.GetStatusText());
    }

    [Fact]
    public void KingWithNoMovesButNotInCheckIsStalemate()
    {
        var board = Load("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.Equal(GameStatus.Stalemate, board.GetStatus());
        Assert.True(board.IsGameOver());
        Assert.Equal("Stalemate!", board.GetStatusText());
    }

    [Fact]
    public void CheckWithEscapeIsCheckNotOver()
    {
        var board = Load("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
        Assert.True(board.ApplyMoveSan("Ra8+"));

        Assert.Equal(GameStatus.Check, board.GetStatus());
        Assert.False(board.IsGameOver());
    }

    [Fact]
    public void StartingPositionIsOngoing()
    {
        var board = ChessBoard.StartingPosition();

        Assert.Equal(GameStatus.Ongoing, board.GetStatus());
        Assert.False(board.IsGameOver());
    }

    [Theory]
    [InlineData("8/8/8/4k3/8/8/8/4K3 w - - 0 1")]          // K v K
    [InlineData("8/8/8/4k3/8/8/8/4KN2 w - - 0 1")]        // K+N v K
    [InlineData("8/8/8/4k3/8/8/8/4KB2 w - - 0 1")]        // K+B v K
    [InlineData("8/8/2b5/4k3/8/8/8/4KB2 w - - 0 1")]      // K+B v K+B, bishops on the same colour (c6, f1)
    public void InsufficientMaterialIsDrawn(string fen)
    {
        Assert.Equal(GameStatus.DrawInsufficientMaterial, Load(fen).GetStatus());
    }

    [Theory]
    [InlineData("8/8/8/4k3/8/8/8/4KR2 w - - 0 1")]        // K+R v K: mate is possible
    [InlineData("8/8/8/4k3/8/8/8/3KNN2 w - - 0 1")]       // K+N+N v K: not a dead position
    [InlineData("8/8/8/2b1k3/8/8/8/4KB2 w - - 0 1")]      // K+B v K+B, bishops on opposite colours (c5, f1)
    public void SufficientMaterialIsNotDrawn(string fen)
    {
        Assert.NotEqual(GameStatus.DrawInsufficientMaterial, Load(fen).GetStatus());
    }

    [Fact]
    public void FiftyMoveRuleTriggersAtHundredHalfMoves()
    {
        Assert.Equal(GameStatus.Ongoing, Load("3k4/8/8/8/8/8/8/3KR3 w - - 99 75").GetStatus());
        Assert.Equal(GameStatus.DrawFiftyMoves, Load("3k4/8/8/8/8/8/8/3KR3 w - - 100 75").GetStatus());
    }

    [Fact]
    public void ThreefoldRepetitionIsDrawn()
    {
        var board = ChessBoard.StartingPosition();
        string[] shuffle = { "Nf3", "Nf6", "Ng1", "Ng8" };

        // The start position occurs at ply 0, 4 and 8: the third occurrence is at ply 8.
        foreach (var san in shuffle) board.ApplyMoveSan(san);
        Assert.Equal(GameStatus.Ongoing, board.GetStatus());

        foreach (var san in shuffle) board.ApplyMoveSan(san);
        Assert.Equal(GameStatus.DrawRepetition, board.GetStatus());
        Assert.Equal("Draw (Repetition)", board.GetStatusText());
    }
}
