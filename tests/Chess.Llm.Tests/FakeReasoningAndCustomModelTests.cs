using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class FakeReasoningAndCustomModelTests
{
    private const string Prompt = "Play a move.\nLegal moves: e4, d4, Nf3";

    [Fact]
    public async Task TheFakeModelsListLowMediumAndHighWithMediumAsDefault()
    {
        var models = await new FakeLlmGateway().ListModelsAsync(CancellationToken.None);

        foreach (var model in models)
        {
            Assert.Equal(["low", "medium", "high"], model.ReasoningEfforts);
            Assert.Equal("medium", model.DefaultReasoningEffort);
            Assert.False(model.Custom);
        }
    }

    [Fact]
    public async Task AConfiguredCustomModelIsListedAsCustomWithNoEffortsAndItsIdAsDefaultName()
    {
        var gateway = new FakeLlmGateway(customModels: [Custom("tiny", displayName: null)]);

        var models = await gateway.ListModelsAsync(CancellationToken.None);

        var tiny = Assert.Single(models, m => m.Id == "tiny");
        Assert.True(tiny.Custom);
        Assert.Equal("tiny", tiny.DisplayName);
        Assert.Null(tiny.ReasoningEfforts);
        Assert.Equal(["fake-a", "fake-b", "tiny"], models.Select(m => m.Id));
    }

    [Fact]
    public async Task AConfiguredCustomModelIsServedOffline()
    {
        var gateway = new FakeLlmGateway(seed: 7, customModels: [Custom("tiny", displayName: "Tiny local")]);
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("tiny", "system", TimeSpan.FromSeconds(5)), CancellationToken.None);

        var reply = await conversation.AskAsync(Prompt, CancellationToken.None);

        Assert.Contains(reply.Text, new[] { "e4", "d4", "Nf3" });
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public async Task AListedEffortStartsAConversation(string effort)
    {
        var gateway = new FakeLlmGateway(seed: 2);

        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-b", "system", TimeSpan.FromSeconds(5), ReasoningEffort: effort), CancellationToken.None);

        Assert.Equal("fake-b", conversation.ModelId);
    }

    [Fact]
    public async Task AnEffortTheFakeModelDoesNotListIsRefused()
    {
        var gateway = new FakeLlmGateway();

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", "system", TimeSpan.FromSeconds(5), ReasoningEffort: "xhigh"), CancellationToken.None));
    }

    [Fact]
    public async Task ACustomModelHasNoEffortsSoAnEffortIsRefused()
    {
        var gateway = new FakeLlmGateway(customModels: [Custom("tiny", displayName: null)]);

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartConversationAsync(
            new LlmConversationOptions("tiny", "system", TimeSpan.FromSeconds(5), ReasoningEffort: "low"), CancellationToken.None));
    }

    [Theory]
    [InlineData("fake-a")]
    [InlineData("fake-b")]
    public void ACustomIdThatClashesWithAFakeModelIsRefusedAtConstruction(string id)
    {
        Assert.Throws<ArgumentException>(() => new FakeLlmGateway(customModels: [Custom(id, displayName: null)]));
    }

    [Fact]
    public async Task CustomModelsOfEveryProviderTypeAreListedWithTheirPricesAndStartOffline()
    {
        var gateway = new FakeLlmGateway(
            customModels:
            [
                new CustomModelOptions { Id = "claude", ProviderType = "anthropic", BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-5-5", ApiKey = "sk-test", UsdPerMillionInput = 3, UsdPerMillionOutput = 15 },
                new CustomModelOptions { Id = "gpt", ProviderType = "openai", BaseUrl = "https://api.openai.com/v1", Model = "gpt-5", CreditsPerMillionInput = 200, CreditsPerMillionOutput = 800 },
                new CustomModelOptions { Id = "deepinfra", ProviderType = "openai", BaseUrl = "https://api.deepinfra.com/v1/openai", Model = "meta-llama/Llama-4" },
            ],
            usdPerAiCredit: 0.01);

        var models = await gateway.ListModelsAsync(CancellationToken.None);

        var claude = models.Single(m => m.Id == "claude");
        Assert.True(claude.Custom);
        Assert.Equal(new LlmTokenPrices(1_000_000, 300, 1500, null), claude.Pricing);
        Assert.Equal(new LlmTokenPrices(1_000_000, 200, 800, null), models.Single(m => m.Id == "gpt").Pricing);
        Assert.Null(models.Single(m => m.Id == "deepinfra").Pricing);

        foreach (var id in new[] { "claude", "gpt", "deepinfra" })
        {
            var conversation = await gateway.StartConversationAsync(new LlmConversationOptions(id, "system", TimeSpan.FromSeconds(5)), CancellationToken.None);
            Assert.Equal(id, conversation.ModelId);
            await conversation.DisposeAsync();
        }
    }

    private static CustomModelOptions Custom(string id, string? displayName) => new()
    {
        Id = id,
        DisplayName = displayName,
        ProviderType = "openai",
        BaseUrl = "http://127.0.0.1:8000/v1",
        Model = "tiny-7b",
    };
}
