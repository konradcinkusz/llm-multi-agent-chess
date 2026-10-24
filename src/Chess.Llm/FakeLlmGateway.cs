namespace Chess.Llm;

/// <summary>
/// Deterministic stand-in for the Copilot gateway. It answers every prompt with one of the moves
/// listed on its <c>Legal moves:</c> line, chosen by a seeded RNG. CI and E2E run on it.
/// </summary>
public sealed class FakeLlmGateway(int seed = 1) : ILlmGateway
{
    private const string LegalMovesPrefix = "Legal moves:";

    private readonly Random _random = new(seed);

    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Fake gateway: no Copilot runtime");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo("fake-a", "Fake A", 0, true),
            new LlmModelInfo("fake-b", "Fake B", 0, true),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        Task.FromResult<ILlmConversation>(new FakeConversation(this, options.ModelId));

    private string PickLegalMove(string prompt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

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

        lock (_random)
        {
            return legal[_random.Next(legal.Length)];
        }
    }

    private sealed class FakeConversation(FakeLlmGateway gateway, string modelId) : ILlmConversation
    {
        public string ModelId { get; } = modelId;

        public Task<LlmReply> AskAsync(string prompt, CancellationToken ct) =>
            Task.FromResult(new LlmReply(gateway.PickLegalMove(prompt, ct), TimeSpan.Zero, null, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
