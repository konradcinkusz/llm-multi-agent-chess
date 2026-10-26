namespace Chess.Llm;

/// <summary>
/// Deterministic stand-in for the Copilot gateway. By default it answers every prompt with one of the
/// moves listed on its <c>Legal moves:</c> line, chosen by a seeded RNG. Replies can be scripted per
/// model with <see cref="Script"/> (for illegal-reply and forfeit tests); scripted replies are returned
/// first, in order, and are not checked against the prompt. CI and E2E run on it.
/// The two fake models take the reasoning efforts <c>low</c>, <c>medium</c> and <c>high</c>. Configured custom models
/// (<c>Arena:CustomModels</c>) are listed and served the same way, without efforts, so the whole custom-model path
/// runs offline (ADR-014).
/// </summary>
/// <param name="seed">Seed for the RNG that picks legal moves.</param>
/// <param name="latency">Simulated delay before every reply; reported as the reply latency.</param>
/// <param name="customModels">Custom models to list and serve next to the two fake models.</param>
public sealed class FakeLlmGateway(int seed = 1, TimeSpan? latency = null, IEnumerable<CustomModelOptions>? customModels = null) : ILlmGateway
{
    private const string LegalMovesPrefix = "Legal moves:";
    private const string MoveLinePrefix = "Move:";
    private const string FakeAId = "fake-a";
    private const string FakeBId = "fake-b";
    private const string DefaultEffort = "medium";
    private static readonly IReadOnlyList<string> FakeEfforts = ["low", "medium", "high"];

    private readonly Random _random = new(seed);
    private readonly Dictionary<string, Queue<string>> _scripted = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeSpan _latency = latency ?? TimeSpan.Zero;
    private readonly IReadOnlyList<CustomModelOptions> _customModels = CheckedCustomModels(customModels);

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
            new LlmModelInfo(FakeAId, "Fake A", 0, true, FakePrices, FakeEfforts, DefaultEffort),
            new LlmModelInfo(FakeBId, "Fake B", 0, true, FakePrices, FakeEfforts, DefaultEffort),
            .. _customModels.Select(c => new LlmModelInfo(c.Id, c.DisplayName ?? c.Id, PremiumMultiplier: null, Enabled: true, Custom: true)),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        if (options.ReasoningEffort is { } effort && !OffersEffort(options.ModelId, effort))
        {
            throw new ArgumentException($"Model {options.ModelId} does not offer reasoning effort '{effort}'.", nameof(options));
        }

        return Task.FromResult<ILlmConversation>(new FakeConversation(this, options.ModelId, options.SystemPrompt));
    }

    private bool OffersEffort(string modelId, string effort) =>
        modelId is FakeAId or FakeBId && FakeEfforts.Contains(effort, StringComparer.Ordinal);

    private static IReadOnlyList<CustomModelOptions> CheckedCustomModels(IEnumerable<CustomModelOptions>? customModels)
    {
        var list = customModels?.ToList() ?? [];
        foreach (var custom in list)
        {
            if (custom.Id is FakeAId or FakeBId)
            {
                throw new ArgumentException($"Custom model id '{custom.Id}' clashes with a fake model.", nameof(customModels));
            }
        }

        return list;
    }

    /// <returns>The reply, and whether it was scripted (a scripted reply is returned as it was queued).</returns>
    private (string Text, bool Scripted) NextReply(string modelId, string prompt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_scripted.TryGetValue(modelId, out var queue) && queue.Count > 0)
            {
                return (queue.Dequeue(), Scripted: true);
            }

            return (PickLegalMove(prompt), Scripted: false);
        }
    }

    /// <summary>
    /// A system prompt that asks for a <c>Move:</c> line (the think style) gets the analysis, plan and move lines in that
    /// order, so the think path runs offline. Any other system prompt gets the bare move, as before.
    /// </summary>
    private static string AnswerFormat(string systemPrompt, string move) =>
        systemPrompt.Contains(MoveLinePrefix, StringComparison.Ordinal)
            ? $"Analysis: the legal moves are listed in the prompt, and {move} is one of them.\nPlan: keep the pieces coordinated.\n{MoveLinePrefix} {move}"
            : move;

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

    private sealed class FakeConversation(FakeLlmGateway gateway, string modelId, string systemPrompt) : ILlmConversation
    {
        public string ModelId { get; } = modelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            var (move, scripted) = gateway.NextReply(ModelId, prompt, ct);
            var text = scripted ? move : AnswerFormat(systemPrompt, move);
            var reasoning = $"The legal moves are listed in the prompt. I choose {move}.";
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
