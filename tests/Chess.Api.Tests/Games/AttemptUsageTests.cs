using Chess.Api.Games;
using Chess.Api.Players;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>03 §10: the reasoning and the usage of a reply reach the stored attempt and the live event, and a rejection keeps its kind.</summary>
public class AttemptUsageTests
{
    private static readonly PlayerDescriptor Llm = new("llm", "fake-a");

    [Fact]
    public async Task AReplyWithUsageIsStoredAndAnnouncedWithItsReasoningAndCost()
    {
        var gateway = new UsageReplyGateway();
        await using var harness = new GameHarness(new ChessPlayerFactory(gateway, Options.Create(new PlayerOptions())), maxPlies: 2);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(Llm, new("llm", "fake-a"));

        var events = await harness.PlayToEndAsync(gameId);

        var attempt = (await harness.LoadAttemptsAsync(gameId)).First(a => a.Ply == 1 && a.Number == 1);
        Assert.True(attempt.Legal);
        Assert.Equal("Reasoning about the move.", attempt.Reasoning);
        Assert.Equal(1000, attempt.InputTokens);
        Assert.Equal(500, attempt.OutputTokens);
        Assert.Equal(300, attempt.ReasoningTokens);
        Assert.Equal(200, attempt.CachedInputTokens);
        Assert.Equal(0.25, attempt.AiCredits);
        Assert.Null(attempt.RejectionKind);

        var announced = events.OfType<AttemptRecorded>().First(r => r.Ply == 1 && r.Attempt == 1);
        Assert.Equal("Reasoning about the move.", announced.Reasoning);
        Assert.Equal(300, announced.ReasoningTokens);
        Assert.Equal(0.25, announced.AiCredits);
        Assert.Null(announced.RejectionKind);
    }

    [Fact]
    public async Task ARejectedReplyKeepsItsKindAndItsUsage()
    {
        var gateway = new UsageReplyGateway(firstReply: "garbage");
        await using var harness = new GameHarness(new ChessPlayerFactory(gateway, Options.Create(new PlayerOptions())), maxPlies: 1);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(Llm, new("llm", "fake-a"));

        var events = await harness.PlayToEndAsync(gameId);

        var rejected = (await harness.LoadAttemptsAsync(gameId)).First(a => a.Ply == 1 && a.Number == 1);
        Assert.False(rejected.Legal);
        Assert.Equal(RejectionClassifier.Unparseable, rejected.RejectionKind);
        Assert.Equal(300, rejected.ReasoningTokens);
        Assert.StartsWith("unparseable: ", rejected.Error);

        var announced = events.OfType<AttemptRecorded>().First(r => r.Ply == 1 && r.Attempt == 1);
        Assert.Equal(RejectionClassifier.Unparseable, announced.RejectionKind);
        Assert.Equal("Reasoning about the move.", announced.Reasoning);
    }

    /// <summary>Every reply reports usage and reasoning; the first one is <paramref name="firstReply"/> when given.</summary>
    private sealed class UsageReplyGateway(string? firstReply = null) : ILlmGateway
    {
        private readonly FakeLlmGateway _fake = new(seed: 9);
        private readonly object _gate = new();
        private bool _firstUsed;

        public LlmGatewayStatus Status => _fake.Status;

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) => _fake.ListModelsAsync(ct);

        public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            new Conversation(await _fake.StartConversationAsync(options, ct), this);

        private string? TakeFirst()
        {
            lock (_gate)
            {
                if (_firstUsed || firstReply is null)
                {
                    return null;
                }

                _firstUsed = true;
                return firstReply;
            }
        }

        private sealed class Conversation(ILlmConversation inner, UsageReplyGateway owner) : ILlmConversation
        {
            public string ModelId => inner.ModelId;

            public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
            {
                var scripted = owner.TakeFirst();
                var reply = scripted is null ? await inner.AskAsync(prompt, ct) : new LlmReply(scripted, TimeSpan.FromMilliseconds(3), null, null);
                return new LlmReply(reply.Text, reply.Latency, 1000, 500,
                    Reasoning: "Reasoning about the move.", ReasoningTokens: 300, CachedInputTokens: 200, AiCredits: 0.25);
            }

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
