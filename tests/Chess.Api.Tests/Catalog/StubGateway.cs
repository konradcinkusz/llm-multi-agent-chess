using Chess.Llm;

namespace Chess.Api.Tests.Catalog;

/// <summary>Gateway double with a fixed status and model list; counts how often the list is requested.</summary>
internal sealed class StubGateway(LlmGatewayStatus status, IReadOnlyList<LlmModelInfo>? models = null, Exception? listFailure = null)
    : ILlmGateway
{
    public LlmGatewayStatus Status { get; } = status;

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        ListCalls++;
        if (listFailure is not null)
        {
            throw listFailure;
        }

        return Task.FromResult(models ?? []);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        throw new NotSupportedException("The catalog never starts conversations.");
}
