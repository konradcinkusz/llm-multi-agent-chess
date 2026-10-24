using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Chess.Api.Tests.Results.ResultsFixtures;
using Fixture = Chess.Api.Tests.Results.ResultsFixtures.BreakdownFixture;

namespace Chess.Api.Tests.Results;

/// <summary>
/// The app on the InMemory provider with its own database, and with the snapshot timer set so far out that
/// no batch is written while a test seeds rows.
/// </summary>
public sealed class ResultsApp : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", DatabaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.UseSetting("RatingSnapshots:Interval", "01:00:00");
    }

    public async Task SeedAsync(Action<ArenaDbContext> seed)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        seed(db);
        await db.SaveChangesAsync();
    }
}

public sealed class RatingHistoryEndpointTests
{
    [Fact]
    public async Task History_is_oldest_first_and_can_be_narrowed_to_one_model()
    {
        using var app = new ResultsApp();
        var game = Guid.NewGuid();
        await app.SeedAsync(db =>
        {
            db.Games.Add(Game(game, "A", "random", "1-0", "checkmate", T0.AddMinutes(1)));
            db.RatingSnapshots.AddRange(
                Snapshot(game, T0.AddMinutes(3), 3, "A", 210),
                Snapshot(game, T0.AddMinutes(1), 1, "A", 150),
                Snapshot(game, T0.AddMinutes(2), 2, "A", 180),
                Snapshot(game, T0.AddMinutes(2), 2, "random", 0));
        });
        var client = app.CreateClient();

        var only = await client.GetFromJsonAsync<List<RatingHistoryPointDto>>("/api/ratings/history?model=A");
        Assert.Equal([1, 2, 3], only!.Select(p => p.RatedGames));
        Assert.Equal([150, 180, 210], only!.Select(p => p.Rating));
        Assert.All(only!, p => Assert.Equal("A", p.Model));

        var everyone = await client.GetFromJsonAsync<List<RatingHistoryPointDto>>("/api/ratings/history");
        Assert.Equal(4, everyone!.Count);
        Assert.Equal(
            new[] { T0.AddMinutes(1), T0.AddMinutes(2), T0.AddMinutes(2), T0.AddMinutes(3) },
            everyone.Select(p => p.TakenAt).ToArray());
    }

    [Fact]
    public async Task History_for_a_model_with_no_snapshots_is_an_empty_list()
    {
        using var app = new ResultsApp();
        var response = await app.CreateClient().GetAsync("/api/ratings/history?model=nobody");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<RatingHistoryPointDto>>())!);
    }
}

public sealed class ResultsBreakdownEndpointTests
{
    [Fact]
    public async Task Breakdown_for_a_tournament_is_served_as_json()
    {
        using var app = new ResultsApp();
        await app.SeedAsync(db => Fixture.Seed(db));

        var rows = await app.CreateClient()
            .GetFromJsonAsync<List<ModelResultsDto>>($"/api/results/breakdown?tournamentId={Fixture.Tournament}");

        var a = rows!.Single(r => r.Model == "A");
        Assert.Equal((4, 1, 1, 2, 2), (a.Games, a.Wins, a.Draws, a.Losses, a.ForfeitLosses));
    }

    [Fact]
    public async Task Breakdown_for_an_unknown_tournament_is_a_404_problem()
    {
        using var app = new ResultsApp();
        var response = await app.CreateClient()
            .GetAsync($"/api/results/breakdown?tournamentId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
