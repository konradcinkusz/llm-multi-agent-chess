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
public sealed record LlmModelInfo(string Id, string DisplayName, double? PremiumMultiplier, bool Enabled);
public sealed record LlmConversationOptions(string ModelId, string SystemPrompt, TimeSpan Timeout);
public sealed record LlmReply(string Text, TimeSpan Latency, int? InputTokens, int? OutputTokens);

public sealed class LlmUnavailableException(string message) : Exception(message);
