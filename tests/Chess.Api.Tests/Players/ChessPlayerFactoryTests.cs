using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Players;

public class ChessPlayerFactoryTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private static IOptions<PlayerOptions> OptionsWith(int maxAttempts = 3) =>
        Options.Create(new PlayerOptions { MaxAttemptsPerMove = maxAttempts });

    [Fact]
    public async Task RandomKindCreatesARandomPlayer()
    {
        var players = new ChessPlayerFactory(new FakeLlmGateway(), OptionsWith());

        var player = await players.CreateAsync(new PlayerDescriptor("random", "random"), Guid.NewGuid(), Side.White, CancellationToken.None);

        Assert.IsType<RandomChessPlayer>(player);
        Assert.Equal("random", player.Descriptor.ModelId);
    }

    [Fact]
    public async Task LlmKindWithFakeGatewayCreatesAnLlmPlayer()
    {
        var players = new ChessPlayerFactory(new FakeLlmGateway(), OptionsWith());

        var player = await players.CreateAsync(new PlayerDescriptor("llm", "fake-a"), Guid.NewGuid(), Side.Black, CancellationToken.None);

        var llm = Assert.IsType<LlmChessPlayer>(player);
        Assert.Equal("fake-a", llm.Descriptor.ModelId);
    }

    [Theory]
    [InlineData(LlmGatewayState.NotAuthenticated)]
    [InlineData(LlmGatewayState.Unavailable)]
    public async Task LlmKindRefusedWhenGatewayIsNotReady(LlmGatewayState state)
    {
        var players = new ChessPlayerFactory(new StubGateway(state), OptionsWith());

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => players.CreateAsync(new PlayerDescriptor("llm", "gpt"), Guid.NewGuid(), Side.White, CancellationToken.None));

        Assert.Equal("Run `copilot` and log in.", error.Message);
    }

    [Fact]
    public async Task UnknownKindIsRejected()
    {
        var players = new ChessPlayerFactory(new FakeLlmGateway(), OptionsWith());

        await Assert.ThrowsAsync<ArgumentException>(
            () => players.CreateAsync(new PlayerDescriptor("human", "me"), Guid.NewGuid(), Side.White, CancellationToken.None));
    }

    [Fact]
    public void AddPlayersRegistersTheFactoryAndBindsTheArenaLimits()
    {
        var configured = app.WithWebHostBuilder(b => b
            .UseSetting("Database:Provider", "InMemory")
            .UseSetting("Llm:Provider", "Fake")
            .UseSetting("Arena:MaxAttemptsPerMove", "5")
            .UseSetting("Arena:MoveTimeoutSeconds", "9"));

        using var scope = configured.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IChessPlayerFactory>();
        var limits = scope.ServiceProvider.GetRequiredService<IOptions<PlayerOptions>>().Value;

        Assert.IsType<ChessPlayerFactory>(players);
        Assert.Equal(5, limits.MaxAttemptsPerMove);
        Assert.Equal(9, limits.MoveTimeoutSeconds);
    }
}
