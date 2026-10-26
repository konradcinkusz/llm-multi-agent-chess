using Chess.Api.Players;
using Chess.Api.Players.Prompts;
using Chess.Engine;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Players;

/// <summary>Counts the conversations a gateway opens, so a test can tell one per game from one per move.</summary>
internal sealed class CountingGateway(ILlmGateway inner) : ILlmGateway
{
    public int Opened { get; private set; }

    public LlmGatewayStatus Status => inner.Status;

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) => inner.ListModelsAsync(ct);

    public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        Opened++;
        return await inner.StartConversationAsync(options, ct);
    }
}

/// <summary>Opens a scripted conversation per call; each conversation takes the next queued reply and records its prompts.</summary>
internal sealed class RecordingGateway : ILlmGateway
{
    public List<ScriptedConversation> Conversations { get; } = [];

    public Queue<string> Replies { get; } = new();

    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Recording gateway");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        var conversation = new ScriptedConversation(ScriptedConversation.Reply(Replies.Dequeue()));
        Conversations.Add(conversation);
        return Task.FromResult<ILlmConversation>(conversation);
    }
}

public class PromptStyleConversationTests
{
    private static ChessPlayerFactory Factory(ILlmGateway gateway) =>
        new(gateway, Options.Create(new PlayerOptions()));

    [Fact]
    public async Task ClassicOpensOneConversationForTheWholeGame()
    {
        var gateway = new CountingGateway(new FakeLlmGateway(seed: 5));
        var player = await Factory(gateway).CreateAsync(new PlayerDescriptor("llm", "fake-a"), Guid.NewGuid(), Side.White, default);

        await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);
        await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);

        Assert.Equal(1, gateway.Opened);
    }

    [Fact]
    public async Task BoardOpensOneConversationPerMove()
    {
        var gateway = new CountingGateway(new FakeLlmGateway(seed: 5));
        var player = await Factory(gateway).CreateAsync(new PlayerDescriptor("llm", "fake-a~board"), Guid.NewGuid(), Side.White, default);
        Assert.Equal(0, gateway.Opened);

        await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);
        await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);

        Assert.Equal(2, gateway.Opened);
    }

    [Fact]
    public async Task ThinkRetriesStayInTheConversationOfTheirMove()
    {
        var inner = new FakeLlmGateway(seed: 5);
        inner.Script("fake-a", "garbage", "Plan: develop\nMove: e4");
        var gateway = new CountingGateway(inner);
        var player = await Factory(gateway).CreateAsync(new PlayerDescriptor("llm", "fake-a~think"), Guid.NewGuid(), Side.White, default);

        var move = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);

        Assert.Equal("e4", move.San);
        Assert.Equal(2, move.Attempts.Count);
        Assert.Equal(1, gateway.Opened);
    }

    [Fact]
    public async Task ThinkCarriesThePlanOfTheLastMoveIntoTheNextPrompt()
    {
        var gateway = new RecordingGateway();
        gateway.Replies.Enqueue("Analysis: open the centre.\nPlan: develop the knight\nMove: e4");
        gateway.Replies.Enqueue("Plan: answer in the centre\nMove: e5");
        var player = await Factory(gateway).CreateAsync(new PlayerDescriptor("llm", "fake-a~think"), Guid.NewGuid(), Side.White, default);

        var first = await player.ChooseMoveAsync(PlayerTestData.StartRequest(), default);
        var second = await player.ChooseMoveAsync(PlayerTestData.RequestFor(PlayerTestData.FenAfter("e4"), ["e4"]), default);

        Assert.Equal("Analysis: open the centre.\nPlan: develop the knight\nMove: e4", first.Attempts[0].RawReply);
        Assert.Equal("e5", second.San);
        Assert.Equal(2, gateway.Conversations.Count);
        Assert.DoesNotContain("Your plan from your last move", gateway.Conversations[0].Prompts[0]);
        Assert.Contains("Your plan from your last move: develop the knight", gateway.Conversations[1].Prompts[0]);
    }

    [Fact]
    public async Task APerMoveStyleCannotBeGivenAConversationForTheGame()
    {
        var gateway = new FakeLlmGateway(seed: 5);
        var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", PromptStyles.Find("board")!.SystemPrompt, TimeSpan.FromSeconds(5)), default);

        Assert.Throws<ArgumentException>(() =>
            new LlmChessPlayer(new PlayerDescriptor("llm", "fake-a~board"), conversation, 3, PromptStyles.Find("board")));
    }
}
