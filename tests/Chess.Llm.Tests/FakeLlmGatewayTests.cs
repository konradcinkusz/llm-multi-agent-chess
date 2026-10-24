using System.Diagnostics;
using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class FakeLlmGatewayTests
{
    private const string Prompt = "Play a move.\nLegal moves: e4, d4, Nf3";

    [Fact]
    public async Task ReportsFakeStatusAndTwoModels()
    {
        var gateway = new FakeLlmGateway();

        Assert.Equal(LlmGatewayState.Fake, gateway.Status.State);

        var models = await gateway.ListModelsAsync(CancellationToken.None);
        Assert.Equal(["fake-a", "fake-b"], models.Select(m => m.Id));
    }

    [Fact]
    public async Task RepliesWithOneOfTheLegalMovesInThePrompt()
    {
        var gateway = new FakeLlmGateway(seed: 7);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var reply = await conversation.AskAsync(Prompt, CancellationToken.None);

        Assert.Contains(reply.Text, new[] { "e4", "d4", "Nf3" });
    }

    [Fact]
    public async Task SameSeedGivesSameSequenceOfReplies()
    {
        var first = await PlayTenMovesAsync(new FakeLlmGateway(seed: 42));
        var second = await PlayTenMovesAsync(new FakeLlmGateway(seed: 42));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PromptWithoutLegalMovesLineIsRejected()
    {
        var gateway = new FakeLlmGateway();
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conversation.AskAsync("no moves here", CancellationToken.None));
    }

    [Fact]
    public async Task ScriptedRepliesComeFirstInOrderAndAreNotCheckedAgainstTheLegalMoves()
    {
        var gateway = new FakeLlmGateway(seed: 3);
        gateway.Script("fake-a", "Zz9", "Qq1");
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        Assert.Equal("Zz9", (await conversation.AskAsync(Prompt, CancellationToken.None)).Text);
        Assert.Equal("Qq1", (await conversation.AskAsync(Prompt, CancellationToken.None)).Text);
        Assert.Contains((await conversation.AskAsync(Prompt, CancellationToken.None)).Text, new[] { "e4", "d4", "Nf3" });
    }

    [Fact]
    public async Task ScriptsAreKeptPerModel()
    {
        var gateway = new FakeLlmGateway(seed: 3);
        gateway.Script("fake-a", "Zz9");
        await using var other = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-b", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var reply = await other.AskAsync(Prompt, CancellationToken.None);

        Assert.Contains(reply.Text, new[] { "e4", "d4", "Nf3" });
    }

    [Fact]
    public async Task SimulatedLatencyDelaysTheReplyAndIsReported()
    {
        var latency = TimeSpan.FromMilliseconds(50);
        var gateway = new FakeLlmGateway(seed: 1, latency);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var clock = Stopwatch.StartNew();
        var reply = await conversation.AskAsync(Prompt, CancellationToken.None);

        Assert.Equal(latency, reply.Latency);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(40), $"Reply came back after {clock.Elapsed}.");
    }

    private static async Task<List<string>> PlayTenMovesAsync(FakeLlmGateway gateway)
    {
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var replies = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            replies.Add((await conversation.AskAsync(Prompt, CancellationToken.None)).Text);
        }

        return replies;
    }
}
