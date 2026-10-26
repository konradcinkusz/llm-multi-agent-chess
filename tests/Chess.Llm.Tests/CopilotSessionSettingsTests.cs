using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class CopilotSessionSettingsTests
{
    private const string Prompt = "Play as White.";

    [Fact]
    public void ACopilotVariantCarriesItsReasoningEffort()
    {
        var config = CopilotSessionSettings.ForCopilotModel(
            new LlmConversationOptions("claude-sonnet-5.5", Prompt, TimeSpan.FromSeconds(5), ReasoningEffort: "medium"));

        Assert.Equal("claude-sonnet-5.5", config.Model);
        Assert.Equal("medium", config.ReasoningEffort);
        Assert.Null(config.Provider);
    }

    [Fact]
    public void ACopilotModelWithoutAVariantLeavesTheEffortToTheModel()
    {
        var config = CopilotSessionSettings.ForCopilotModel(
            new LlmConversationOptions("claude-sonnet-5.5", Prompt, TimeSpan.FromSeconds(5)));

        Assert.Null(config.ReasoningEffort);
    }

    [Fact]
    public void ACustomModelIsStartedWithTheProviderConfigAndNoCopilotModelId()
    {
        var custom = new CustomModelOptions
        {
            Id = "tiny",
            ProviderType = "OpenAI",
            BaseUrl = "http://127.0.0.1:8000/v1",
            Model = "tiny-7b-finetune",
            WireApi = "chat-completions",
            ApiKey = "sk-test-secret",
        };

        var config = CopilotSessionSettings.ForCustomModel(
            new LlmConversationOptions("tiny", Prompt, TimeSpan.FromSeconds(5), ReasoningEffort: "high"), custom);

        Assert.Null(config.Model);
        Assert.Equal("high", config.ReasoningEffort);
        Assert.NotNull(config.Provider);
        Assert.Equal("openai", config.Provider.Type);
        Assert.Equal("http://127.0.0.1:8000/v1", config.Provider.BaseUrl);
        Assert.Equal("chat-completions", config.Provider.WireApi);
        Assert.Equal("tiny-7b-finetune", config.Provider.WireModel);
        Assert.Equal("sk-test-secret", config.Provider.ApiKey);
    }

    [Theory]
    [InlineData("anthropic", "https://api.anthropic.com", "anthropic", null)]
    [InlineData("openai", "https://api.openai.com/v1", "openai", "chat-completions")]
    [InlineData("openai", "https://api.deepinfra.com/v1/openai", "openai", "chat-completions")]
    [InlineData("azure", "https://example.openai.azure.com", "azure_openai", "chat-completions")]
    public void EachApiKeyProviderGetsItsTypeBaseUrlModelWireApiAndTelemetryProvider(
        string providerType, string baseUrl, string modelProvider, string? wireApi)
    {
        var custom = new CustomModelOptions
        {
            Id = "hosted",
            ProviderType = providerType,
            BaseUrl = baseUrl,
            Model = "hosted-model",
            WireApi = "chat-completions",
            ApiKey = "key-for-the-test",
        };

        var config = CopilotSessionSettings.ForCustomModel(
            new LlmConversationOptions("hosted", Prompt, TimeSpan.FromSeconds(5)), custom);

        Assert.Null(config.Model);
        Assert.Equal(providerType, config.Provider!.Type);
        Assert.Equal(baseUrl, config.Provider.BaseUrl);
        Assert.Equal("hosted-model", config.Provider.WireModel);
        Assert.Equal(modelProvider, config.Provider.ModelProvider);
        Assert.Equal(wireApi, config.Provider.WireApi);
        Assert.Equal("key-for-the-test", config.Provider.ApiKey);
    }

    [Fact]
    public void AnAnthropicProviderTypeIsLowerCasedWhateverTheConfiguredCase()
    {
        var custom = new CustomModelOptions { Id = "claude", ProviderType = "Anthropic", BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-5-5" };

        var config = CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("claude", Prompt, TimeSpan.FromSeconds(5)), custom);

        Assert.Equal("anthropic", config.Provider!.Type);
        Assert.Equal("anthropic", config.Provider.ModelProvider);
    }

    [Fact]
    public void EverySessionHasNoToolsAndTheReplacedSystemPrompt()
    {
        var copilot = CopilotSessionSettings.ForCopilotModel(new LlmConversationOptions("fake-a", Prompt, TimeSpan.FromSeconds(5)));
        var custom = CopilotSessionSettings.ForCustomModel(
            new LlmConversationOptions("tiny", Prompt, TimeSpan.FromSeconds(5)),
            new CustomModelOptions { Id = "tiny", BaseUrl = "http://127.0.0.1:8000/v1", Model = "tiny-7b" });

        foreach (var config in new[] { copilot, custom })
        {
            Assert.Empty(config.AvailableTools!);
            Assert.False(config.Streaming);
            Assert.Equal(Prompt, config.SystemMessage!.Content);
            Assert.NotNull(config.OnPermissionRequest);
        }
    }

    [Fact]
    public void ListedEffortsKeepTheModelsOrderAndAModelWithNoneListsNull()
    {
        Assert.Equal(["low", "medium", "high"], CopilotSessionSettings.ListedEfforts(["low", "medium", "high"]));
        Assert.Null(CopilotSessionSettings.ListedEfforts([]));
        Assert.Null(CopilotSessionSettings.ListedEfforts(null));
    }
}
