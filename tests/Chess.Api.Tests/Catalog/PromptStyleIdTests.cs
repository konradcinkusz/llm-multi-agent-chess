using System.Net;
using System.Net.Http.Json;
using Chess.Api.Benchmarks;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Players;
using Chess.Api.Players.Prompts;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Catalog;

public class PromptStyleIdTests
{
    private static readonly ModelListing Listing = new(
    [
        new ModelDto("fake-a", "Fake A", 0, true, null, ["low", "medium", "high"], "medium"),
        new ModelDto("random", "Random", 0, true),
    ], Warning: null);

    [Theory]
    [InlineData("fake-a~think")]
    [InlineData("fake-a~board")]
    [InlineData("fake-a@high~think")]
    [InlineData("fake-a@medium~board")]
    public void AStyledIdIsFine(string id)
    {
        Assert.Null(ModelVariant.Problem(id, Listing));
    }

    [Theory]
    [InlineData("fake-a~classic", "write the plain id")]
    [InlineData("fake-a~nope", "not a prompt style")]
    [InlineData("fake-a@high~think@low", "not a player id")]
    [InlineData("fake-a~think@high", "not a player id")]
    [InlineData("~think", "not a player id")]
    [InlineData("fake-a~", "not a player id")]
    [InlineData("fake-a~think~board", "not a player id")]
    [InlineData("fake-a@turbo~think", "does not offer reasoning effort 'turbo'")]
    [InlineData("random~think", "takes no prompt style")]
    public void AnUnusableStyledIdIsRefusedWithItsReason(string id, string reason)
    {
        Assert.Contains(reason, ModelVariant.Problem(id, Listing));
    }

    [Fact]
    public void ThePartsOfAStyledIdAreReadAndAPlainIdIsClassic()
    {
        Assert.Equal(new ModelVariant.Parts("fake-a", "high", "think"), ModelVariant.Parse("fake-a@high~think"));
        Assert.Equal(new ModelVariant.Parts("fake-a", null, "board"), ModelVariant.Parse("fake-a~board"));
        Assert.Equal(new ModelVariant.Parts("fake-a", null, "classic"), ModelVariant.Parse("fake-a"));
        Assert.Equal("fake-a", ModelVariant.BaseOf("fake-a@high~think"));
        Assert.Equal("fake-a", ModelVariant.BaseOf("fake-a~board"));
        Assert.Equal("think", ModelVariant.StyleOf("fake-a@high~think"));
        Assert.Equal("classic", ModelVariant.StyleOf("fake-a@high"));
    }

    [Fact]
    public async Task TheFactoryBuildsAStyledPlayerAndRefusesAnUnknownStyle()
    {
        var factory = new ChessPlayerFactory(new FakeLlmGateway(), Options.Create(new PlayerOptions()));

        var player = await factory.CreateAsync(new PlayerDescriptor("llm", "fake-a~think"), Guid.NewGuid(), Chess.Engine.Side.White, default);
        Assert.Equal("fake-a~think", player.Descriptor.ModelId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateAsync(new PlayerDescriptor("llm", "fake-a~nope"), Guid.NewGuid(), Chess.Engine.Side.White, default));
    }

    [Fact]
    public void TheBenchmarkFingerprintCarriesTheCandidatesStyleVersion()
    {
        Assert.Equal(PromptStyles.VersionOf(PromptStyles.Find("think")!), BenchmarkFingerprint.PromptVersion("fake-a~think"));
        Assert.Equal(PromptStyles.VersionOf(PromptStyles.Classic), BenchmarkFingerprint.PromptVersion("fake-a"));
        Assert.NotEqual(BenchmarkFingerprint.PromptVersion(), BenchmarkFingerprint.PromptVersion("fake-a~board"));
    }
}

public sealed class PromptStyleEndpointTests(FakeGatewayApp app) : IClassFixture<FakeGatewayApp>
{
    [Fact]
    public async Task ThePromptStylesAreListedClassicFirstWithTheirVersions()
    {
        var response = await app.CreateClient().GetAsync("/api/prompt-styles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var styles = (await response.Content.ReadFromJsonAsync<List<PromptStyleDto>>())!;
        Assert.Equal(["classic", "board", "think"], styles.Select(s => s.Id));
        Assert.Equal([false, true, true], styles.Select(s => s.PerMoveConversation));
        Assert.Equal(PromptStyles.All.Select(PromptStyles.VersionOf), styles.Select(s => s.Version));
        Assert.Equal("sha256:9b00ea00ef62", styles[0].Version);
    }
}
