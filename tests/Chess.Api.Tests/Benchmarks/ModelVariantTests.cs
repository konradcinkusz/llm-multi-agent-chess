using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

public class ModelVariantTests
{
    private static readonly ModelListing Listing = new(
    [
        new ModelDto("fake-a", "Fake A", 0, true, null, ["low", "medium", "high"], "medium"),
        new ModelDto("plain", "Plain", 1, true),
    ], Warning: null);

    [Fact]
    public void AVariantSplitsIntoItsModelAndEffort()
    {
        Assert.Equal(("fake-a", "high"), ModelVariant.Split("fake-a@high"));
        Assert.Equal(("fake-a", null), ModelVariant.Split("fake-a"));
        Assert.Equal("fake-a", ModelVariant.BaseOf("fake-a@high"));
    }

    [Theory]
    [InlineData("a@b@c")]
    [InlineData("@high")]
    [InlineData("fake-a@")]
    [InlineData("")]
    public void AMalformedIdIsRefused(string id)
    {
        Assert.Throws<ArgumentException>(() => ModelVariant.Split(id));
        Assert.NotNull(ModelVariant.Problem(id, Listing));
    }

    [Fact]
    public void AnEffortTheModelOffersIsFine()
    {
        Assert.Null(ModelVariant.Problem("fake-a@medium", Listing));
    }

    [Fact]
    public void AnEffortTheModelDoesNotOfferNamesTheOnesItOffers()
    {
        var problem = ModelVariant.Problem("fake-a@turbo", Listing);

        Assert.Equal("fake-a does not offer reasoning effort 'turbo'. It offers: low, medium, high.", problem);
    }

    [Fact]
    public void AModelNotListedCannotHaveItsEffortChecked()
    {
        Assert.Contains("not a model the gateway offers", ModelVariant.Problem("gone@high", Listing));
    }

    [Fact]
    public async Task TheFactoryPassesTheEffortToTheGateway()
    {
        var factory = new ChessPlayerFactory(new FakeLlmGateway(), Options.Create(new PlayerOptions()));

        var player = await factory.CreateAsync(new PlayerDescriptor("llm", "fake-a@high"), Guid.NewGuid(), Side.White, default);

        Assert.Equal("fake-a@high", player.Descriptor.ModelId);
    }

    [Fact]
    public async Task TheFactoryRefusesAnEffortTheGatewayDoesNotOffer()
    {
        var factory = new ChessPlayerFactory(new FakeLlmGateway(), Options.Create(new PlayerOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateAsync(new PlayerDescriptor("llm", "fake-a@turbo"), Guid.NewGuid(), Side.White, default));
    }
}
