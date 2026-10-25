namespace Chess.Llm;

public interface ILlmGateway
{
    LlmGatewayStatus Status { get; }
    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct);
    Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct);
}

public interface ILlmConversation : IAsyncDisposable
{
    string ModelId { get; }
    Task<LlmReply> AskAsync(string prompt, CancellationToken ct);
}

public enum LlmGatewayState { Ready, NotAuthenticated, Unavailable, Fake }
public sealed record LlmGatewayStatus(LlmGatewayState State, string Detail);
public sealed record LlmModelInfo(string Id, string DisplayName, double? PremiumMultiplier, bool Enabled, LlmTokenPrices? Pricing = null);

/// <summary>A model's token prices in AI credits per <see cref="BatchSize"/> tokens, as the Copilot runtime lists them (03 §10).</summary>
public sealed record LlmTokenPrices(long BatchSize, double? InputCredits, double? OutputCredits, double? CachedInputCredits);
public sealed record LlmConversationOptions(string ModelId, string SystemPrompt, TimeSpan Timeout);
/// <summary>
/// One reply. <see cref="Reasoning"/> is the model's readable reasoning when it exposes one; <see cref="AiCredits"/> is the
/// cost the runtime reported for this reply in AI credits (null when it reported none). 03 §10.
/// </summary>
public sealed record LlmReply(string Text, TimeSpan Latency, int? InputTokens, int? OutputTokens,
    string? Reasoning = null, int? ReasoningTokens = null, int? CachedInputTokens = null, double? AiCredits = null);

public sealed class LlmUnavailableException(string message) : Exception(message);
