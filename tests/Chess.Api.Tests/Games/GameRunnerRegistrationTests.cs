using Chess.Api.Games;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Games;

public class GameRunnerRegistrationTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void AddGameRunnerRegistersTheRunnerAsItsOwnWorkerAndBindsTheArenaLimits()
    {
        var configured = app.WithWebHostBuilder(b => b
            .UseSetting("Database:Provider", "InMemory")
            .UseSetting("Llm:Provider", "Fake")
            .UseSetting("Arena:MaxConcurrentGames", "3")
            .UseSetting("Arena:MaxPlies", "42"));

        using var scope = configured.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IGameRunner>();
        var worker = scope.ServiceProvider.GetServices<IHostedService>().OfType<GameRunner>().Single();
        var limits = scope.ServiceProvider.GetRequiredService<IOptions<GameRunnerOptions>>().Value;

        Assert.Same(worker, runner);
        Assert.IsType<InMemoryGameEventStream>(scope.ServiceProvider.GetRequiredService<IGameEventStream>());
        Assert.Equal(3, limits.MaxConcurrentGames);
        Assert.Equal(42, limits.MaxPlies);
    }
}
