using Chess.Web.Api;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Components.Shared;
using Xunit;

namespace Chess.Web.Tests.Games;

public class ReplayTests
{
    private static readonly GameSummaryDto Summary = new(Guid.Empty, null, new PlayerSpec("llm", "gpt"),
        new PlayerSpec("llm", "claude"), true, "finished", "1-0", "checkmate", 2, DateTimeOffset.UnixEpoch, null);

    private static MoveDto Move(int ply, string color, string san, string fenAfter) =>
        new(ply, color, san, fenAfter, 1, 0, null, null, null, null);

    private static GameDetailDto Game(string startFen, params MoveDto[] moves) =>
        new(Summary, startFen, moves.Length == 0 ? startFen : moves[^1].FenAfter, moves);

    [Fact]
    public void FenAtReadsTheStartPositionThenThePositionAfterEachMove()
    {
        var game = Game(Fixtures.StartFen,
            Move(1, "white", "e4", Fixtures.AfterE4),
            Move(2, "black", "e5", Fixtures.AfterE4E5));

        Assert.Equal(Fixtures.StartFen, Replay.FenAt(game, 0));
        Assert.Equal(Fixtures.AfterE4, Replay.FenAt(game, 1));
        Assert.Equal(Fixtures.AfterE4E5, Replay.FenAt(game, 2));
    }

    [Fact]
    public void ClampKeepsThePlyBetweenTheStartAndTheLastMove()
    {
        Assert.Equal(0, Replay.Clamp(-1, 2));
        Assert.Equal(1, Replay.Clamp(1, 2));
        Assert.Equal(2, Replay.Clamp(5, 2));
    }

    [Fact]
    public void NoMoveIsHighlightedAtTheStartPosition()
    {
        var game = Game(Fixtures.StartFen, Move(1, "white", "e4", Fixtures.AfterE4));

        Assert.Null(Replay.LastMove(game, 0));
    }

    [Fact]
    public void AWhiteMoveHighlightsItsFromAndToSquares()
    {
        var game = Game(Fixtures.StartFen, Move(1, "white", "e4", Fixtures.AfterE4));

        Assert.Equal(new BoardMove("e2", "e4"), Replay.LastMove(game, 1));
    }

    [Fact]
    public void ABlackMoveHighlightsItsFromAndToSquares()
    {
        var game = Game(Fixtures.StartFen,
            Move(1, "white", "e4", Fixtures.AfterE4),
            Move(2, "black", "e5", Fixtures.AfterE4E5));

        Assert.Equal(new BoardMove("e7", "e5"), Replay.LastMove(game, 2));
    }

    [Fact]
    public void CastlingHighlightsTheKingsMoveNotTheRooks()
    {
        const string before = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        const string after = "r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1";

        Assert.Equal(new BoardMove("e1", "g1"), MoveSquares.Between(before, after, "white"));
    }

    [Fact]
    public void APromotionHighlightsThePawnsMove()
    {
        const string before = "8/P7/8/8/8/8/8/k6K w - - 0 1";
        const string after = "Q7/8/8/8/8/8/8/k6K b - - 0 1";

        Assert.Equal(new BoardMove("a7", "a8"), MoveSquares.Between(before, after, "white"));
    }

    [Fact]
    public void APositionThatDidNotChangeHasNoMoveToHighlight()
    {
        Assert.Null(MoveSquares.Between(Fixtures.StartFen, Fixtures.StartFen, "white"));
    }
}
