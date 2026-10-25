using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Players;

/// <summary>03 §10.3: each rejection kind, as the player records it in the attempt's reason text.</summary>
public class RejectionReasonTests
{
    private static readonly PlayerDescriptor Descriptor = new("llm", "fake-a");
    private static readonly LlmConversationOptions Options =
        new("fake-a", RatedPromptBuilder.SystemPrompt, TimeSpan.FromSeconds(5));

    // White's rooks on a7 and f7 both reach c7, so "Rc7" names two legal moves (Rac7 and Rfc7).
    private const string TwoRooksOnSeventh = "4k3/R4R2/8/8/8/8/8/4K3 w - - 0 1";

    [Fact]
    public async Task ProseAroundTheMoveIsUnparseable()
    {
        var conversation = new ScriptedConversation(ScriptedConversation.Reply(
            "I think a sensible developing move here is Nf3, which keeps the centre under control."));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 1);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        var attempt = Assert.Single(move.Attempts);
        Assert.False(attempt.Legal);
        Assert.StartsWith("unparseable: ", attempt.Error);
        Assert.Contains("is not one move", attempt.Error);
    }

    [Fact]
    public async Task ALegalLookingMoveThatIsNotLegalIsIllegalAndNamesTheCandidate()
    {
        var conversation = new ScriptedConversation(ScriptedConversation.Reply("Qxf7"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 1);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        var attempt = Assert.Single(move.Attempts);
        Assert.Equal("illegal: \"Qxf7\" is not a legal move in this position.", attempt.Error);
    }

    [Fact]
    public async Task AnAmbiguousSanNamesEveryLegalMoveItMatches()
    {
        var conversation = new ScriptedConversation(ScriptedConversation.Reply("Rc7+"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 1);
        var request = PlayerTestData.RequestFor(TwoRooksOnSeventh, []);

        var move = await player.ChooseMoveAsync(request, CancellationToken.None);

        var attempt = Assert.Single(move.Attempts);
        Assert.StartsWith("ambiguous: \"Rc7\" matches 2 legal moves (", attempt.Error);
        Assert.Contains("Rac7", attempt.Error);
        Assert.Contains("Rfc7", attempt.Error);
    }

    [Fact]
    public async Task ATimeoutIsRecordedWithItsKind()
    {
        var conversation = new ScriptedConversation(
            () => throw new TimeoutException("Model fake-a did not finish its reply within 00:03:00."));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 1);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        var attempt = Assert.Single(move.Attempts);
        Assert.Equal("timeout: Model fake-a did not finish its reply within 00:03:00.", attempt.Error);
        Assert.Null(attempt.RawReply);
    }

    [Fact]
    public async Task AGatewayFailureIsRecordedAsAnErrorWithTheExceptionName()
    {
        var conversation = new ScriptedConversation(() => throw new InvalidOperationException("the runtime exited"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 1);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        Assert.Equal("error: InvalidOperationException: the runtime exited", Assert.Single(move.Attempts).Error);
    }

    [Fact]
    public void ReasonTextTruncatesAProseReplyToEightyCharacters()
    {
        var board = new ChessBoard();
        board.LoadFen(PlayerTestData.StartFen);
        var prose = new string('x', 300);

        var rejection = RejectionClassifier.Classify(board, prose);

        Assert.Equal(RejectionClassifier.Unparseable, rejection.Kind);
        Assert.Contains(new string('x', 80) + "...", rejection.Reason);
        Assert.DoesNotContain(new string('x', 81), rejection.Reason);
    }

    [Theory]
    [InlineData("1. Nf3", "Nf3")]
    [InlineData("\"Nf3\"", "Nf3")]
    [InlineData("0-0", "O-O")]
    [InlineData("Nf3!", "Nf3")]
    public void TheCandidateIsTheMoveWithoutDecoration(string reply, string expected)
    {
        Assert.Equal(expected, RejectionClassifier.Candidate(reply));
    }
}
