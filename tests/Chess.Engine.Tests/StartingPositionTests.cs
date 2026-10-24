using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class StartingPositionTests
{
    [Fact]
    public void StartPositionHasTwentyLegalMoves()
    {
        var board = ChessBoard.StartingPosition();

        Assert.Equal(20, board.GetLegalMoves().Count);
    }
}
