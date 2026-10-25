using GitHub.Copilot;

namespace Chess.Llm;

/// <summary>
/// The SDK session configuration of one conversation (03 §12.1, ADR-014). Internal, so the tests can check it without a
/// runtime; no SDK type leaves this assembly.
/// </summary>
internal static class CopilotSessionSettings
{
    /// <summary>A Copilot model: its id, and the effort of the variant (null = the model's default effort).</summary>
    public static SessionConfig ForCopilotModel(LlmConversationOptions options) =>
        Create(options, model: options.ModelId, provider: null);

    /// <summary>A custom model: the SDK's bring-your-own-key provider, with no Copilot model id.</summary>
    public static SessionConfig ForCustomModel(LlmConversationOptions options, CustomModelOptions custom) =>
        Create(options, model: null, provider: new ProviderConfig
        {
            Type = custom.ProviderType.ToLowerInvariant(),
            WireApi = custom.WireApi,
            BaseUrl = custom.BaseUrl,
            ApiKey = custom.ApiKey,
            WireModel = custom.Model,
        });

    /// <summary>The reasoning efforts a model lists, or null when it lists none.</summary>
    public static IReadOnlyList<string>? ListedEfforts(IEnumerable<string>? efforts) =>
        efforts?.ToList() is { Count: > 0 } list ? list : null;

    private static SessionConfig Create(LlmConversationOptions options, string? model, ProviderConfig? provider) => new()
    {
        Model = model,
        ReasoningEffort = options.ReasoningEffort,
        Provider = provider,
        SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = options.SystemPrompt },
        // No tools: an empty allow-list, and every permission request is rejected as a second guard.
        AvailableTools = [],
        Streaming = false,
        OnPermissionRequest = RejectAllPermissions,
    };

#pragma warning disable GHCP001 // The SDK marks its permission decision type experimental; it is the only way to reject a request.
    private static Task<GitHub.Copilot.Rpc.PermissionDecision> RejectAllPermissions(PermissionRequest request, PermissionInvocation invocation) =>
        Task.FromResult(GitHub.Copilot.Rpc.PermissionDecision.Reject("Chess arena: this session has no tools."));
#pragma warning restore GHCP001
}
