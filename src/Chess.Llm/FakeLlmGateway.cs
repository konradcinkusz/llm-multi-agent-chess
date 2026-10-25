namespace Chess.Llm;

/// <summary>
/// Deterministic stand-in for the Copilot gateway. By default it answers every prompt with one of the
/// moves listed on its <c>Legal moves:</c> line, chosen by a seeded RNG. Replies can be scripted per
/// model with <see cref="Script"/> (for illegal-reply and forfeit tests); scripted replies are returned
/// first, in order, and are not checked against the prompt. CI and E2E run on it.
/// </summary>
/// <param name="seed">Seed for the RNG that picks legal moves.</param>
/// <param name="latency">Simulated delay before every reply; reported as the reply latency.</param>
public sealed class FakeLlmGateway(int seed = 1, TimeSpan? latency = null) : ILlmGateway
{
    private const string LegalMovesPrefix = "Legal moves:";

    private readonly Random _random = new(seed);
    private readonly Dictionary<string, Queue<string>> _scripted = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeSpan _latency = latency ?? TimeSpan.Zero;

    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Fake gateway: no Copilot runtime");

    /// <summary>Queues replies for <paramref name="modelId"/>; they are consumed first, oldest first.</summary>
    public void Script(string modelId, params string[] replies)
    {
        lock (_gate)
        {
            if (!_scripted.TryGetValue(modelId, out var queue))
            {
                queue = new Queue<string>();
                _scripted[modelId] = queue;
            }

            foreach (var reply in replies)
            {
                queue.Enqueue(reply);
            }
        }
    }

    /// <summary>Prices of both fake models: 1.0 and 4.0 AI credits per 1 000 000 input and output tokens.</summary>
    private static readonly LlmTokenPrices FakePrices = new(1_000_000, 1.0, 4.0, null);

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo("fake-a", "Fake A", 0, true, FakePrices),
            new LlmModelInfo("fake-b", "Fake B", 0, true, FakePrices),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        Task.FromResult<ILlmConversation>(new FakeConversation(this, options.ModelId));

    private string NextReply(string modelId, string prompt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_scripted.TryGetValue(modelId, out var queue) && queue.Count > 0)
            {
                return queue.Dequeue();
            }

            return PickLegalMove(prompt);
        }
    }

    private string PickLegalMove(string prompt)
    {
        var line = prompt.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(LegalMovesPrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Prompt has no '{LegalMovesPrefix}' line.");

        var legal = line[LegalMovesPrefix.Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (legal.Length == 0)
        {
            throw new InvalidOperationException($"'{LegalMovesPrefix}' line lists no moves.");
        }

        return legal[_random.Next(legal.Length)];
    }

    private sealed class FakeConversation(FakeLlmGateway gateway, string modelId) : ILlmConversation
    {
        public string ModelId { get; } = modelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            var text = gateway.NextReply(ModelId, prompt, ct);
            var reasoning = $"The legal moves are listed in the prompt. I choose {text}.";
            if (gateway._latency > TimeSpan.Zero)
            {
                await Task.Delay(gateway._latency, ct);
            }

            // Usage is approximated from the text (about four characters per token). Reasoning tokens are part of the output.
            var reasoningTokens = ApproximateTokens(reasoning);
            var inputTokens = ApproximateTokens(prompt);
            var outputTokens = ApproximateTokens(text) + reasoningTokens;
            var aiCredits = (inputTokens * FakePrices.InputCredits + outputTokens * FakePrices.OutputCredits) / FakePrices.BatchSize;

            return new LlmReply(text, gateway._latency, inputTokens, outputTokens,
                reasoning, reasoningTokens, CachedInputTokens: null, aiCredits);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static int ApproximateTokens(string text) => text.Length / 4;
    }
}
