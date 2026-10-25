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

    [Fact]
    public async Task FakeModelsListPricesInCreditsPerMillionTokens()
    {
        var models = await new FakeLlmGateway().ListModelsAsync(CancellationToken.None);

        foreach (var model in models)
        {
            Assert.Equal(new LlmTokenPrices(1_000_000, 1.0, 4.0, null), model.Pricing);
        }
    }

    [Fact]
    public async Task EveryReplyReportsNonZeroUsageAndReasoningThatNamesTheMove()
    {
        var gateway = new FakeLlmGateway(seed: 9);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var reply = await conversation.AskAsync(Prompt, CancellationToken.None);

        Assert.True(reply.InputTokens > 0, "input tokens");
        Assert.True(reply.OutputTokens > 0, "output tokens");
        Assert.True(reply.ReasoningTokens > 0, "reasoning tokens");
        Assert.True(reply.AiCredits > 0, "AI credits");
        Assert.NotNull(reply.Reasoning);
        Assert.Contains(reply.Text, reply.Reasoning);
    }

    [Fact]
    public async Task UsageFollowsTheApproximationAndThePricesOfTheFakeModel()
    {
        var gateway = new FakeLlmGateway(seed: 9);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var reply = await conversation.AskAsync(Prompt, CancellationToken.None);

        // Input is the prompt at four characters per token; output is the reply plus the reasoning, which is part of the output.
        Assert.Equal(Prompt.Length / 4, reply.InputTokens);
        Assert.Equal(reply.Text.Length / 4 + reply.ReasoningTokens, reply.OutputTokens);
        Assert.Equal((reply.InputTokens * 1.0 + reply.OutputTokens * 4.0) / 1_000_000, reply.AiCredits);
        Assert.Null(reply.CachedInputTokens);
    }

    [Fact]
    public async Task UsageAndReasoningAreDeterministicForTheSameSeed()
    {
        var first = await PlayTenRepliesAsync(new FakeLlmGateway(seed: 42));
        var second = await PlayTenRepliesAsync(new FakeLlmGateway(seed: 42));

        Assert.Equal(first, second);
    }

    private static async Task<List<LlmReply>> PlayTenRepliesAsync(FakeLlmGateway gateway)
    {
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var replies = new List<LlmReply>();
        for (var i = 0; i < 10; i++)
        {
            replies.Add(await conversation.AskAsync(Prompt, CancellationToken.None));
        }

        return replies;
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
