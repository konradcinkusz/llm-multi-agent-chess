using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Llm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Catalog;

public class ModelCatalogTests
{
    private static readonly LlmModelInfo[] Models =
    [
        new("model-1", "Model One", 1.0, true),
        new("model-2", "Model Two", null, false),
    ];

    private static readonly LlmGatewayStatus Ready = new(LlmGatewayState.Ready, "signed in");

    [Fact]
    public async Task ReadyGatewayListsItsModelsThenRandomWithoutWarning()
    {
        var catalog = Catalog(new StubGateway(Ready, Models), TimeSpan.FromSeconds(60));

        var listing = await catalog.GetModelsAsync(default);

        Assert.Equal(["model-1", "model-2", "random"], listing.Models.Select(m => m.Id));
        Assert.Equal(new ModelDto("model-1", "Model One", 1.0, true), listing.Models[0]);
        Assert.Equal(new ModelDto("model-2", "Model Two", null, false), listing.Models[1]);
        Assert.Null(listing.Warning);
    }

    [Theory]
    [InlineData(LlmGatewayState.NotAuthenticated)]
    [InlineData(LlmGatewayState.Unavailable)]
    public async Task NotReadyGatewayListsOnlyRandomWithWarningAndDoesNotAskTheGateway(LlmGatewayState state)
    {
        var gateway = new StubGateway(new LlmGatewayStatus(state, "not ready"), Models);
        var catalog = Catalog(gateway, TimeSpan.FromSeconds(60));

        var listing = await catalog.GetModelsAsync(default);

        Assert.Equal(["random"], listing.Models.Select(m => m.Id));
        Assert.NotNull(listing.Warning);
        Assert.Contains(state.ToString(), listing.Warning);
        Assert.Equal(0, gateway.ListCalls);
    }

    [Fact]
    public async Task ModelsAreCachedWithinTheTtl()
    {
        var gateway = new StubGateway(Ready, Models);
        var catalog = Catalog(gateway, TimeSpan.FromSeconds(60));

        await catalog.GetModelsAsync(default);
        await catalog.GetModelsAsync(default);

        Assert.Equal(1, gateway.ListCalls);
    }

    [Fact]
    public async Task ModelsAreRequestedAgainOnceTheTtlHasPassed()
    {
        var gateway = new StubGateway(Ready, Models);
        var catalog = Catalog(gateway, TimeSpan.FromMilliseconds(50));

        await catalog.GetModelsAsync(default);
        await Task.Delay(200);
        await catalog.GetModelsAsync(default);

        Assert.Equal(2, gateway.ListCalls);
    }

    [Fact]
    public async Task LlmUnavailableWhileListingFallsBackToRandomAndIsNotCached()
    {
        var gateway = new StubGateway(Ready, listFailure: new LlmUnavailableException("runtime stopped"));
        var catalog = Catalog(gateway, TimeSpan.FromSeconds(60));

        var first = await catalog.GetModelsAsync(default);
        var second = await catalog.GetModelsAsync(default);

        Assert.Equal(["random"], first.Models.Select(m => m.Id));
        Assert.NotNull(first.Warning);
        Assert.Equal(["random"], second.Models.Select(m => m.Id));
        Assert.Equal(2, gateway.ListCalls);
    }

    private static ModelCatalog Catalog(ILlmGateway gateway, TimeSpan ttl) =>
        new(gateway, new MemoryCache(new MemoryCacheOptions()), Options.Create(new CatalogOptions { ModelCacheTtl = ttl }));
}
