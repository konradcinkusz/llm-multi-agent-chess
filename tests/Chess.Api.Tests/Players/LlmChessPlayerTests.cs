using Chess.Api.Players;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Players;

public class LlmChessPlayerTests
{
    private static readonly PlayerDescriptor Descriptor = new("llm", "fake-a");
    private static readonly LlmConversationOptions ConversationOptions =
        new("fake-a", RatedPromptBuilder.SystemPrompt, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task SecondReplyIsTakenAfterOneIllegalReplyWithTwoAttempts()
    {
        var gateway = new FakeLlmGateway(seed: 7);
        gateway.Script("fake-a", "garbage", "e4");
        await using var conversation = await gateway.StartConversationAsync(ConversationOptions, CancellationToken.None);
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        Assert.Equal("e4", move.San);
        Assert.False(move.Forfeit);
        Assert.Equal(2, move.Attempts.Count);
        Assert.False(move.Attempts[0].Legal);
        Assert.Equal("garbage", move.Attempts[0].RawReply);
        Assert.True(move.Attempts[1].Legal);
        Assert.Equal("e4", move.Attempts[1].ParsedSan);
    }

    [Fact]
    public async Task ThreeIllegalRepliesForfeitTheMove()
    {
        var gateway = new FakeLlmGateway(seed: 7);
        gateway.Script("fake-a", "Qxf7", "nonsense", "e9");
        await using var conversation = await gateway.StartConversationAsync(ConversationOptions, CancellationToken.None);
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        Assert.Null(move.San);
        Assert.True(move.Forfeit);
        Assert.Equal(3, move.Attempts.Count);
        Assert.All(move.Attempts, a => Assert.False(a.Legal));
        Assert.Equal(new[] { 1, 2, 3 }, move.Attempts.Select(a => a.Number).ToArray());
    }

    [Fact]
    public async Task FirstAttemptIsTheRatedPromptAndLaterAttemptsAreCorrective()
    {
        var conversation = new ScriptedConversation(
            ScriptedConversation.Reply("garbage"),
            ScriptedConversation.Reply("d4"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);
        var request = PlayerTestData.StartRequest();

        var move = await player.ChooseMoveAsync(request, CancellationToken.None);

        Assert.Equal("d4", move.San);
        Assert.Equal(RatedPromptBuilder.Build(request), conversation.Prompts[0]);
        Assert.Equal(RatedPromptBuilder.Corrective("garbage", request.LegalSan), conversation.Prompts[1]);
        Assert.Equal(RatedPromptBuilder.Build(request), move.Attempts[0].Prompt);
    }

    [Fact]
    public async Task TimeoutCountsAsAnAttemptAndTheGameGoesOn()
    {
        var conversation = new ScriptedConversation(
            () => throw new TimeoutException("No reply within 60 s."),
            ScriptedConversation.Reply("Nf3"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        Assert.Equal("Nf3", move.San);
        Assert.Equal(2, move.Attempts.Count);
        Assert.False(move.Attempts[0].Legal);
        Assert.Null(move.Attempts[0].RawReply);
        Assert.Equal("timeout: No reply within 60 s.", move.Attempts[0].Error);
        Assert.Equal("No move was received.", conversation.Prompts[1].Split('\n')[0]);
    }

    [Fact]
    public async Task ExceptionsOnEveryAttemptForfeitTheMove()
    {
        var conversation = new ScriptedConversation(
            () => throw new LlmUnavailableException("runtime gone"),
            () => throw new LlmUnavailableException("runtime gone"));
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 2);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), CancellationToken.None);

        Assert.True(move.Forfeit);
        Assert.Equal(2, move.Attempts.Count);
        Assert.All(move.Attempts, a => Assert.Equal("error: LlmUnavailableException: runtime gone", a.Error));
    }

    [Fact]
    public async Task CancellationIsNotRecordedAsAnAttempt()
    {
        using var cts = new CancellationTokenSource();
        var conversation = new ScriptedConversation(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => player.ChooseMoveAsync(PlayerTestData.StartRequest(), cts.Token));
    }

    [Fact]
    public async Task FakeGatewayWithoutScriptPlaysALegalMoveFromThePrompt()
    {
        var gateway = new FakeLlmGateway(seed: 3);
        await using var conversation = await gateway.StartConversationAsync(ConversationOptions, CancellationToken.None);
        var player = new LlmChessPlayer(Descriptor, conversation, maxAttempts: 3);
        var request = PlayerTestData.StartRequest();

        var move = await player.ChooseMoveAsync(request, CancellationToken.None);

        Assert.False(move.Forfeit);
        Assert.Single(move.Attempts);
        Assert.Contains(move.San, request.LegalSan);
    }

    [Fact]
    public void RejectsAttemptLimitBelowOne()
    {
        var conversation = new ScriptedConversation();

        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmChessPlayer(Descriptor, conversation, maxAttempts: 0));
    }
}
