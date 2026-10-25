namespace Chess.Llm;

/// <summary>
/// Sums the usage the runtime reports for one reply (03 §10.2). It takes plain values, read from the SDK events by
/// <see cref="CopilotLlmGateway"/>, so the aggregation can be tested without a session.
/// </summary>
internal sealed class UsageTally
{
    /// <summary>nano-AI units per AI credit (the SDK's <c>TotalNanoAiu</c> is in nano-AIU; 1 AIU = 1 credit is assumed, README).</summary>
    internal const double NanoAiuPerAiCredit = 1_000_000_000;

    private long? _inputTokens;
    private long? _outputTokens;
    private long? _reasoningTokens;
    private long? _cachedInputTokens;
    private double? _nanoAiu;

    /// <summary>
    /// Adds one usage event. A null value means the event did not report that number, and it adds nothing.
    /// <paramref name="totalNanoAiu"/> is null when the event carried no copilot usage. An event whose credits are
    /// <paramref name="creditsUnavailable"/> adds no cost: missing billing is not a zero charge.
    /// </summary>
    public void Add(long? inputTokens, long? outputTokens, long? reasoningTokens, long? cachedInputTokens,
        double? totalNanoAiu, bool creditsUnavailable)
    {
        _inputTokens = Sum(_inputTokens, inputTokens);
        _outputTokens = Sum(_outputTokens, outputTokens);
        _reasoningTokens = Sum(_reasoningTokens, reasoningTokens);
        _cachedInputTokens = Sum(_cachedInputTokens, cachedInputTokens);

        if (totalNanoAiu is { } nanoAiu && !creditsUnavailable)
        {
            _nanoAiu = (_nanoAiu ?? 0) + nanoAiu;
        }
    }

    public ReplyUsage Snapshot() => new(
        ToInt(_inputTokens),
        ToInt(_outputTokens),
        ToInt(_reasoningTokens),
        ToInt(_cachedInputTokens),
        _nanoAiu is { } nanoAiu ? nanoAiu / NanoAiuPerAiCredit : null);

    private static long? Sum(long? total, long? value) =>
        value is { } v ? (total ?? 0) + v : total;

    private static int? ToInt(long? value) => value is { } v ? checked((int)v) : null;
}

/// <summary>The usage of one reply. Null fields were not reported by the runtime.</summary>
internal sealed record ReplyUsage(int? InputTokens, int? OutputTokens, int? ReasoningTokens, int? CachedInputTokens, double? AiCredits);
