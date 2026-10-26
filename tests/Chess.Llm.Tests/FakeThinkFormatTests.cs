using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class FakeThinkFormatTests
{
    private const string Prompt = "You are playing a game of chess.\nLegal moves: e4, d4\nAnswer with one move in SAN only.";

    [Fact]
    public async Task ASystemPromptThatAsksForAMoveLineGetsTheThinkFormat()
    {
        var gateway = new FakeLlmGateway(seed: 2);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "Think, then end with the line `Move: <SAN>`.", TimeSpan.FromSeconds(5)), default);

        var reply = await conversation.AskAsync(Prompt, default);

        var lines = reply.Text.Split('\n');
        Assert.StartsWith("Analysis:", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Plan:", lines[1], StringComparison.Ordinal);
        Assert.Matches("^Move: (e4|d4)$", lines[2]);
    }

    [Fact]
    public async Task AnotherSystemPromptGetsTheBareMove()
    {
        var gateway = new FakeLlmGateway(seed: 2);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "Each reply is a single move.", TimeSpan.FromSeconds(5)), default);

        var reply = await conversation.AskAsync(Prompt, default);

        Assert.Contains(reply.Text, new[] { "e4", "d4" });
    }

    [Fact]
    public async Task AScriptedReplyIsReturnedAsItWasQueued()
    {
        var gateway = new FakeLlmGateway(seed: 2);
        gateway.Script("fake-a", "garbage");
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "Think, then end with the line `Move: <SAN>`.", TimeSpan.FromSeconds(5)), default);

        var reply = await conversation.AskAsync(Prompt, default);

        Assert.Equal("garbage", reply.Text);
    }
}
