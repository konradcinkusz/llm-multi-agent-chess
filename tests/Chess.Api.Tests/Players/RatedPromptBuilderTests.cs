using Chess.Api.Players;
using Chess.Engine;
using Xunit;

namespace Chess.Api.Tests.Players;

public class RatedPromptBuilderTests
{
    [Fact]
    public void BuildMatchesTheRatedTemplateExactly()
    {
        var fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var request = new MoveRequest(Guid.Empty, 5, fen, ["Bb5", "Bc4", "d3"], ["e4", "e5", "Nf3", "Nc6"]);

        var prompt = RatedPromptBuilder.Build(request);

        Assert.Equal(
            """
            You are playing a game of chess. It is White to move.
            Position (FEN): r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3
            Last moves: 1. e4 e5 2. Nf3 Nc6
            Legal moves: Bb5, Bc4, d3
            Answer with one move in SAN only.
            """.ReplaceLineEndings("\n"),
            prompt.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void StartPositionHasNoLastMoves()
    {
        var request = PlayerTestData.StartRequest();

        var prompt = RatedPromptBuilder.Build(request);

        Assert.Contains("Last moves: none\n", prompt);
        Assert.Contains("It is White to move.", prompt);
    }

    [Fact]
    public void ShowsOnlyTheLastTenPliesWithCorrectMoveNumbers()
    {
        // 1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 — twelve plies, White to move at move 7.
        var history = new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7", "Re1", "b5" };
        var request = PlayerTestData.RequestFor(PlayerTestData.FenAfter(history), history);

        var prompt = RatedPromptBuilder.Build(request);

        Assert.Contains("Last moves: 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5\n", prompt);
    }

    [Fact]
    public void WindowStartingWithBlackMoveUsesEllipsisForThatMove()
    {
        // Eleven plies, Black to move: the window of ten starts with Black's 1...e5.
        var history = new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7", "Re1" };
        var request = PlayerTestData.RequestFor(PlayerTestData.FenAfter(history), history);

        var prompt = RatedPromptBuilder.Build(request);

        Assert.Contains("It is Black to move.", prompt);
        Assert.Contains("Last moves: 1... e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1\n", prompt);
    }

    [Fact]
    public void CorrectiveFollowUpNamesTheRejectedReplyAndTheLegalMoves()
    {
        var text = RatedPromptBuilder.Corrective("Qxf7", ["e4", "Nf3"]);

        Assert.Equal(
            "'Qxf7' is not a legal move.\nLegal moves: e4, Nf3\nAnswer with one move in SAN only.",
            text);
    }

    [Fact]
    public void CorrectiveFollowUpForAMissingReplyDoesNotQuoteAnything()
    {
        var text = RatedPromptBuilder.Corrective(null, ["e4"]);

        Assert.StartsWith("No move was received.\n", text);
    }
}
