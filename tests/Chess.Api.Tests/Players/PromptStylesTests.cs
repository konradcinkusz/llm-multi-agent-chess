using Chess.Api.Benchmarks;
using Chess.Api.Players;
using Chess.Api.Players.Prompts;
using Chess.Engine;
using Xunit;

namespace Chess.Api.Tests.Players;

public class PromptStylesTests
{
    /// <summary>The classic prompt version as the benchmark fingerprints carried it before styles existed (ADR-015: byte-identical texts).</summary>
    private const string ClassicVersion = "sha256:9b00ea00ef62";

    [Fact]
    public void TheClassicVersionIsTheOneTheFingerprintsAlreadyCarry()
    {
        Assert.Equal(ClassicVersion, BenchmarkFingerprint.PromptVersion());
        Assert.Equal(ClassicVersion, PromptStyles.VersionOf(PromptStyles.Classic));
    }

    [Fact]
    public void TheClassicMovePromptIsTodaysTextByteForByte()
    {
        var request = new MoveRequest(Guid.Empty, 0, ChessBoard.StartingPosition().ToFen(), ["e4", "d4"], []);

        var prompt = PromptStyles.Classic.Build(request, plan: null);

        Assert.Equal(string.Join('\n',
            "You are playing a game of chess. It is White to move.",
            "Position (FEN): rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "Last moves: none",
            "Legal moves: e4, d4",
            "Answer with one move in SAN only."), prompt);
        Assert.Equal("You are playing a game of chess against another language model. " +
            "Each reply is a single move in standard algebraic notation (SAN) and nothing else.", PromptStyles.Classic.SystemPrompt);
    }

    [Fact]
    public void TheStylesAreListedClassicFirstAndEachHasItsOwnVersion()
    {
        Assert.Equal(["classic", "board", "think"], PromptStyles.All.Select(s => s.Id));
        var versions = PromptStyles.All.Select(PromptStyles.VersionOf).ToList();
        Assert.Equal(3, versions.Distinct().Count());
        Assert.All(versions, v => Assert.Matches("^sha256:[0-9a-f]{12}$", v));
    }

    [Fact]
    public void TheBoardPromptIsTheGoldenTextForTheStartPosition()
    {
        // The legal moves are the engine's, and the style sorts them alphabetically (ordinal).
        var request = PlayerTestData.StartRequest();

        var prompt = PromptStyles.Find("board")!.Build(request, plan: null);

        Assert.Equal(string.Join('\n',
            "You are playing a game of chess. It is White to move.",
            "Position (FEN): rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "Board (rank 8 at the top, White in upper case):",
            "8 r n b q k b n r",
            "7 p p p p p p p p",
            "6 . . . . . . . .",
            "5 . . . . . . . .",
            "4 . . . . . . . .",
            "3 . . . . . . . .",
            "2 P P P P P P P P",
            "1 R N B Q K B N R",
            "  a b c d e f g h",
            "White: Ke1, Qd1, Ra1, Rh1, Bc1, Bf1, Nb1, Ng1, pawns a2, b2, c2, d2, e2, f2, g2, h2",
            "Black: Ke8, Qd8, Ra8, Rh8, Bc8, Bf8, Nb8, Ng8, pawns a7, b7, c7, d7, e7, f7, g7, h7",
            "Material: White 39, Black 39 (pawn 1, knight and bishop 3, rook 5, queen 9). Balance: level.",
            "Game (PGN moves): none",
            "Legal moves: Na3, Nc3, Nf3, Nh3, a3, a4, b3, b4, c3, c4, d3, d4, e3, e4, f3, f4, g3, g4, h3, h4",
            "Answer with one move in SAN only."), prompt);
    }

    [Fact]
    public void TheBoardPromptGivesTheWholeGameAndNoEngineHelp()
    {
        var history = new[] { "e4", "e5", "Nf3" };
        var fen = PlayerTestData.FenAfter(history);
        var request = PlayerTestData.RequestFor(fen, history);

        var prompt = PromptStyles.Find("board")!.Build(request, plan: null);

        Assert.Contains("Game (PGN moves): 1. e4 e5 2. Nf3", prompt);
        Assert.Single(prompt.Split('\n'), line => line.StartsWith("Legal moves:", StringComparison.Ordinal));
        Assert.DoesNotContain("attack", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("best", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheThinkPromptCarriesThePlanOnlyWhenThereIsOne()
    {
        var request = PlayerTestData.StartRequest();
        var think = PromptStyles.Find("think")!;

        Assert.DoesNotContain("Your plan from your last move", think.Build(request, plan: null));
        Assert.Contains("Your plan from your last move: keep the rook active",
            think.Build(request, plan: "keep the rook active"));
        Assert.Contains("`Move: <SAN>`", think.Build(request, plan: null));
    }

    [Theory]
    [InlineData("Analysis: the knight is free.\nPlan: develop the knight\nMove: Nf3", "Nf3")]
    [InlineData("Move: e5\nRe-checking the options.\nMove: Nf3", "Nf3")]
    [InlineData("move: Nf3", "Nf3")]
    [InlineData("Nf3", "Nf3")]
    [InlineData("**Move:** Nf3", "Nf3")]
    [InlineData("Move: **Nf3**", "Nf3")]
    [InlineData("- Move: Nf3", "Nf3")]
    [InlineData("1. **Move:** Nf3", "Nf3")]
    [InlineData("> Move: Nf3", "Nf3")]
    [InlineData("Analysis.\n```\nMove: Nf3\n```", "Nf3")]
    public void TheMoveIsTheTextAfterTheLastMoveLine(string reply, string expected)
    {
        Assert.Equal(expected, PromptStyles.Find("think")!.MoveText(reply));
    }

    [Theory]
    [InlineData("**Plan:** castle and push the c-pawn", "castle and push the c-pawn")]
    [InlineData("- Plan: castle", "castle")]
    [InlineData("Plan: **castle**\nMove: O-O", "castle")]
    public void TheMarkdownAroundThePlanLabelAndValueIsIgnored(string reply, string expected)
    {
        Assert.Equal(expected, PromptStyles.Find("think")!.PlanOf(reply));
    }

    [Fact]
    public void AReplyWithNoMoveLineIsReadWhole()
    {
        var reply = "I would like to develop my pieces.";

        var move = PromptStyles.Find("think")!.MoveText(reply);

        Assert.Equal(reply, move);
        var board = new ChessBoard();
        board.LoadFen(ChessBoard.StartingPosition().ToFen());
        Assert.Equal(RejectionClassifier.Unparseable, RejectionClassifier.Classify(board, move).Kind);
    }

    [Fact]
    public void ThePlanIsTheLastPlanLineAndIsCappedAt300Characters()
    {
        var think = PromptStyles.Find("think")!;
        var overlong = new string('x', 400);

        Assert.Equal("castle", think.PlanOf("Plan: develop\nPlan: castle\nMove: O-O"));
        Assert.Null(think.PlanOf("Move: e4"));
        Assert.Equal(300, think.PlanOf($"Plan: {overlong}\nMove: e4")!.Length);
    }

    [Fact]
    public void TheCorrectiveNamesTheRejectedMoveAndAsksForTheTwoLines()
    {
        var text = PromptStyles.Find("think")!.Corrective("Qxf7", ["e4", "Nf3"]);

        Assert.StartsWith("'Qxf7' is not a legal move.\nLegal moves: e4, Nf3\n", text, StringComparison.Ordinal);
        Assert.Contains("`Plan: <one sentence>`", text);
        Assert.Contains("`Move: <SAN>`", text);
    }

    [Fact]
    public void ClassicAndBoardReadTheWholeReply()
    {
        const string reply = "e4";

        Assert.Equal(reply, PromptStyles.Classic.MoveText(reply));
        Assert.Equal(reply, PromptStyles.Find("board")!.MoveText(reply));
        Assert.Null(PromptStyles.Classic.PlanOf(reply));
    }
}
