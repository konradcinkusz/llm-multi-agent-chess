using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Benchmarks;
using Chess.Api.Contracts;
using Chess.Api.Players.Prompts;
using Chess.Api.Tests.Tournaments;
using Chess.Api.Tournaments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

/// <summary>The app over the InMemory provider and the fake gateway, with the real fake players: its games run to their ends.</summary>
public sealed class FakeRunApp : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", _databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
    }
}

public class BenchmarkEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreatingABenchmarkReturnsItWithItsEstimateAndLocation()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/benchmarks",
            new CreateBenchmarkRequest("Medium pick", "fake-a@high", ["fake-b", "random"], 4, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BenchmarkCreatedDto>(Json);
        Assert.NotNull(created);
        Assert.Equal($"/api/benchmarks/{created.Benchmark.Id}", response.Headers.Location!.ToString());
        Assert.Equal(TournamentStatus.Running, created.Benchmark.Status);
        Assert.Equal("fake-a@high", created.Benchmark.Candidate);
        Assert.Equal(["fake-b", "random"], created.Benchmark.Opponents);
        Assert.Equal(8, created.Benchmark.GamesTotal);
        Assert.Equal(["fake-a@high", "fake-b", "random"], created.EstimatedPremiumRequests.Select(e => e.ModelId));

        // The candidate's cost is priced as its model: the variant has no prices of its own.
        var candidateEstimate = created.EstimatedPremiumRequests[0];
        Assert.NotNull(candidateEstimate.EstimatedUsd);
        Assert.Equal("assumed", candidateEstimate.TokenBasis);

        var listed = await client.GetFromJsonAsync<List<BenchmarkDto>>("/api/benchmarks", Json);
        Assert.Contains(listed!, b => b.Id == created.Benchmark.Id);

        // A benchmark is not a round robin, so the tournament list leaves it out.
        var tournaments = await client.GetFromJsonAsync<List<TournamentDto>>("/api/tournaments", Json);
        Assert.DoesNotContain(tournaments!, t => t.Id == created.Benchmark.Id);
    }

    [Fact]
    public async Task WithoutOpponentsTheRunPlaysEveryOtherEnabledModel()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/benchmarks", new CreateBenchmarkRequest("Pick", "fake-a@high", null, 2));

        var created = await response.Content.ReadFromJsonAsync<BenchmarkCreatedDto>(Json);
        Assert.Equal(["fake-b", "random"], created!.Benchmark.Opponents);
        Assert.Equal(4, created.Benchmark.GamesTotal);
    }

    [Fact]
    public async Task AnEffortTheModelDoesNotOfferIsRefusedWithAProblem()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/benchmarks",
            new CreateBenchmarkRequest("Pick", "fake-a@turbo", ["fake-b"], 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("turbo", problem.GetProperty("errors").GetProperty("candidate")[0].GetString());
    }

    [Fact]
    public async Task AnOddNumberOfGamesPerOpponentIsRefused()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/benchmarks",
            new CreateBenchmarkRequest("Pick", "fake-a", ["fake-b"], 3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ATournamentWithAnUnofferedEffortIsRefused()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tournaments", new CreateTournamentRequest("Pick",
            ["fake-a@turbo", "fake-b"], 2, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancellingARunAbortsItAndMarksItCancelled()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();
        var created = await CreateAsync(client, new CreateBenchmarkRequest("Pick", "fake-a@high", ["fake-b"], 4, 1));

        var response = await client.PostAsync($"/api/benchmarks/{created.Benchmark.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<BenchmarkDto>(Json);
        Assert.Equal(TournamentStatus.Cancelled, cancelled!.Status);
        Assert.NotNull(cancelled.FinishedAt);

        // The runner records an aborted game when it stops it, which is after the cancel request has returned.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        BenchmarkDto settled;
        do
        {
            settled = (await client.GetFromJsonAsync<BenchmarkDto>($"/api/benchmarks/{created.Benchmark.Id}", Json))!;
            await Task.Delay(100);
        }
        while (settled.GamesFinished < settled.GamesTotal && DateTime.UtcNow < deadline);
        Assert.Equal(settled.GamesTotal, settled.GamesFinished);
        Assert.Equal(TournamentStatus.Cancelled, settled.Status);
    }

    [Fact]
    public async Task AnUnknownBenchmarkIsNotFound()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/benchmarks/{unknown}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/benchmarks/{unknown}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/benchmarks/{unknown}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task AFakeBenchmarkFinishesAndItsReportTotalsEqualItsGames()
    {
        using var app = new FakeRunApp();
        using var client = app.CreateClient();
        var created = await CreateAsync(client, new CreateBenchmarkRequest("Fake pick", "fake-a@high", ["fake-b", "random"], 4, 2));

        var finished = await WaitForAsync(client, created.Benchmark.Id);
        Assert.Equal(TournamentStatus.Finished, finished.Status);
        Assert.Equal(8, finished.GamesFinished);

        var report = await client.GetFromJsonAsync<BenchmarkReportDto>($"/api/benchmarks/{created.Benchmark.Id}/report", Json);
        Assert.NotNull(report);
        Assert.Equal(finished.GamesTotal, report.Summary.Games);
        Assert.Equal(report.Summary.Games, report.Opponents.Sum(o => o.Games));
        Assert.All(report.Opponents, o => Assert.Equal(4, o.Games));
        Assert.All(report.Opponents, o => Assert.Equal(o.Games, o.Wins + o.Draws + o.Losses));
        Assert.Equal(["fake-b", "random"], report.Opponents.Select(o => o.Opponent));
        Assert.NotEmpty(report.Fingerprint.AppVersion);
        Assert.Equal("1", report.Fingerprint.FormulaVersion);
        Assert.StartsWith("sha256:", report.Fingerprint.PromptVersion);

        var markdown = await client.GetAsync($"/api/benchmarks/{created.Benchmark.Id}/report?format=md");
        Assert.Equal("text/markdown; charset=utf-8", markdown.Content.Headers.ContentType!.ToString());
        var text = await markdown.Content.ReadAsStringAsync();
        Assert.Contains("| fake-b | 4 |", text);
        Assert.Contains("| random | 4 |", text);
        Assert.Contains($"Prompt version: {report.Fingerprint.PromptVersion}", text);
    }

    [Fact]
    public async Task AStyledCandidatesReportCarriesTheVersionOfItsStyle()
    {
        using var app = new FakeRunApp();
        using var client = app.CreateClient();
        var created = await CreateAsync(client, new CreateBenchmarkRequest("Think pick", "fake-a~think", ["fake-b"], 2, 2));
        await WaitForAsync(client, created.Benchmark.Id);

        var report = await client.GetFromJsonAsync<BenchmarkReportDto>($"/api/benchmarks/{created.Benchmark.Id}/report", Json);

        Assert.NotNull(report);
        Assert.Equal(PromptStyles.VersionOf(PromptStyles.Find("think")!), report.Fingerprint.PromptVersion);
    }

    [Fact]
    public async Task AReportInAnUnknownFormatIsRefused()
    {
        using var app = new TournamentApp();
        using var client = app.CreateClient();
        var created = await CreateAsync(client, new CreateBenchmarkRequest("Pick", "fake-a", ["fake-b"], 2));

        var response = await client.GetAsync($"/api/benchmarks/{created.Benchmark.Id}/report?format=csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<BenchmarkCreatedDto> CreateAsync(HttpClient client, CreateBenchmarkRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/benchmarks", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BenchmarkCreatedDto>(Json))!;
    }

    private static async Task<BenchmarkDto> WaitForAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            var benchmark = await client.GetFromJsonAsync<BenchmarkDto>($"/api/benchmarks/{id}", Json);
            if (benchmark!.Status != TournamentStatus.Running || DateTime.UtcNow > deadline)
            {
                return benchmark;
            }

            await Task.Delay(100);
        }
    }
}
