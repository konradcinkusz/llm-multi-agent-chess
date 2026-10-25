using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Contracts;
using Chess.Api.Players;
using Chess.Api.Tests.Games;
using Chess.Api.Tournaments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

/// <summary>
/// The app over the InMemory provider and the fake gateway. Every player waits for its move forever, so a game ends only
/// when a test cancels it, and the tournament's state is known at each step.
/// </summary>
public sealed class TournamentApp : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", _databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChessPlayerFactory>();
            services.AddSingleton<IChessPlayerFactory>(new ScriptedPlayerFactory(
                (descriptor, _) => new ScriptedPlayer(descriptor, WaitForever)));
        });
    }

    private static async Task<PlayerMove> WaitForever(MoveRequest request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("Unreachable: the delay only ends when the game is cancelled.");
    }
}

public class TournamentEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreatingATournamentReturnsItWithItsEstimateAndItsLocation()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tournaments", Request(["fake-a", "fake-b", "random"], 2, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TournamentCreatedDto>(Json);
        Assert.NotNull(created);
        Assert.Equal($"/api/tournaments/{created.Tournament.Id}", response.Headers.Location!.ToString());
        Assert.Equal(TournamentStatus.Running, created.Tournament.Status);
        Assert.Equal(6, created.Tournament.GamesTotal);
        Assert.Equal(0, created.Tournament.GamesFinished);
        Assert.Equal(["fake-a", "fake-b", "random"], created.EstimatedPremiumRequests.Select(e => e.ModelId));

        var detail = await client.GetFromJsonAsync<TournamentDto>(response.Headers.Location!.ToString(), Json);
        Assert.Equal(created.Tournament.Id, detail!.Id);
        Assert.Equal(6, detail.GamesTotal);

        var listed = await client.GetFromJsonAsync<List<TournamentDto>>("/api/tournaments", Json);
        Assert.Contains(listed!, t => t.Id == created.Tournament.Id);
    }

    [Fact]
    public async Task AnOddNumberOfGamesPerPairIsRefusedWithAProblem()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tournaments", Request(["fake-a", "fake-b"], 3, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("gamesPerPair", out _));
    }

    [Fact]
    public async Task AMaxConcurrencyAboveTheRunnerLimitIsRefusedWithAProblem()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        // The default Arena:MaxConcurrentGames is 2.
        var response = await client.PostAsJsonAsync("/api/tournaments", Request(["fake-a", "fake-b"], 2, 3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("maxConcurrency", out _));
    }

    [Fact]
    public async Task CancellingEndsTheTournamentAndEveryGameOfItEnds()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tournaments", Request(["fake-a", "fake-b", "random"], 2, 2));
        var created = await response.Content.ReadFromJsonAsync<TournamentCreatedDto>(Json);
        var url = $"/api/tournaments/{created!.Tournament.Id}";

        var cancelled = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var immediately = await cancelled.Content.ReadFromJsonAsync<TournamentDto>(Json);
        Assert.Equal(TournamentStatus.Cancelled, immediately!.Status);

        // Two games were in the runner and are aborted there; the other four are closed at once. All six end.
        var ended = await EventuallyAsync(async () =>
        {
            var tournament = await client.GetFromJsonAsync<TournamentDto>(url, Json);
            return tournament!.GamesFinished == tournament.GamesTotal ? tournament : null;
        });
        Assert.Equal(TournamentStatus.Cancelled, ended.Status);
        Assert.Equal(6, ended.GamesFinished);
        Assert.NotNull(ended.FinishedAt);
    }

    [Fact]
    public async Task CostInputsGivesTheRateAndTheAssumedTokensAndNoModelsBeforeAnyMoveIsStored()
    {
        await using var app = new TournamentApp();
        using var client = app.CreateClient();

        var inputs = await client.GetFromJsonAsync<CostInputsDto>("/api/cost/inputs", Json);

        Assert.NotNull(inputs);
        Assert.Equal(0.01, inputs.UsdPerAiCredit);
        Assert.Equal(700, inputs.AssumedInputTokensPerMove);
        Assert.Equal(300, inputs.AssumedOutputTokensPerMove);
        Assert.Empty(inputs.Models);
    }

    [Fact]
    public async Task AnUnknownTournamentIsNotFound()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();
        var url = $"/api/tournaments/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url)).StatusCode);
    }

    private static CreateTournamentRequest Request(string[] models, int gamesPerPair, int maxConcurrency) =>
        new("Spring", models, gamesPerPair, maxConcurrency);

    /// <summary>Polls until the read returns a value, or fails after 20 seconds.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> read) where T : class
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (true)
        {
            var value = await read();
            if (value is not null)
            {
                return value;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The tournament did not reach the expected state in time.");
            }

            await Task.Delay(50);
        }
    }
}
