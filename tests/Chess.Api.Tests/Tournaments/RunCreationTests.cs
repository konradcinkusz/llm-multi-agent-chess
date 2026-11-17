using System.Net;
using System.Net.Http.Json;
using Chess.Api.Benchmarks;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Results;
using Chess.Api.Tests.Catalog;
using Chess.Api.Tournaments;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

/// <summary>
/// Creating a tournament or a benchmark makes the checks <c>POST /api/games</c> makes, has upper bounds, and saves nothing when
/// it cannot finish (the estimate is computed before any game is created). A benchmark keeps the versions that played it.
/// </summary>
public class RunCreationTests
{
    private static readonly ModelListing Listing = new(
    [
        new ModelDto("fake-a", "Fake A", 0, true),
        new ModelDto("fake-b", "Fake B", 0, true),
        new ModelDto("random", "Random", 0, true),
    ], Warning: null);

    private static readonly ModelListing NotReady = new([new ModelDto("random", "Random", 0, true)],
        Warning: "LLM gateway is NotAuthenticated; only the random player is available");

    private static string[] Models(int count) => Enumerable.Range(0, count).Select(i => $"model-{i}").ToArray();

    // ---- Upper bounds ----

    [Fact]
    public void A_round_robin_has_at_most_MaxModels_models()
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("T", Models(TournamentRules.MaxModels + 1), 2, 1), 2);

        Assert.Contains($"{TournamentRules.MaxModels}", Assert.Single(errors["models"]));
        Assert.Empty(TournamentRules.Validate(new CreateTournamentRequest("T", Models(TournamentRules.MaxModels), 2, 1), 2));
    }

    [Fact]
    public void Games_per_pair_are_bounded_even_when_the_count_would_overflow()
    {
        foreach (var gamesPerPair in new[] { TournamentRules.MaxGamesPerPair + 2, int.MaxValue - 1 })
        {
            var errors = TournamentRules.Validate(new CreateTournamentRequest("T", ["fake-a", "fake-b"], gamesPerPair, 1), 2);

            Assert.True(errors.ContainsKey("gamesPerPair"));
        }
    }

    [Fact]
    public void A_round_robin_may_not_create_more_than_MaxGames_games_in_all()
    {
        // 16 models make 120 pairs; 20 games each would be 2400 games.
        var errors = TournamentRules.Validate(new CreateTournamentRequest("T", Models(16), 20, 1), 2);

        Assert.Contains($"{TournamentRules.MaxGames}", Assert.Single(errors["gamesPerPair"]));
        Assert.Empty(TournamentRules.Validate(new CreateTournamentRequest("T", Models(16), 16, 1), 2));
    }

    [Fact]
    public void A_benchmark_has_bounds_on_its_opponents_and_its_games()
    {
        var tooMany = BenchmarkRules.Validate(new CreateBenchmarkRequest("B", "fake-a", null, 2, 1),
            Models(BenchmarkRules.MaxOpponents + 1), ListingWith(BenchmarkRules.MaxOpponents + 1), 2);
        Assert.True(tooMany.ContainsKey("opponents"));

        var tooLong = BenchmarkRules.Validate(new CreateBenchmarkRequest("B", "fake-a", null, BenchmarkRules.MaxGamesPerOpponent + 2, 1),
            ["fake-b"], Listing, 2);
        Assert.True(tooLong.ContainsKey("gamesPerOpponent"));

        var tooBig = BenchmarkRules.Validate(new CreateBenchmarkRequest("B", "fake-a", null, 100, 1),
            Models(25), ListingWith(25), 2);
        Assert.Contains($"{TournamentRules.MaxGames}", Assert.Single(tooBig["gamesPerOpponent"]));

        Assert.Empty(BenchmarkRules.Validate(new CreateBenchmarkRequest("B", "fake-a", null, 30, 1), Models(40), ListingWith(40), 2));
    }

    private static ModelListing ListingWith(int count) =>
        new([.. Listing.Models, .. Models(count).Select(id => new ModelDto(id, id, 0, true))], Warning: null);

    // ---- The gateway must be ready for an LLM ----

    [Fact]
    public void A_run_with_an_llm_is_refused_while_the_gateway_is_not_ready_but_a_run_of_random_players_is_not()
    {
        Assert.NotNull(TournamentRules.GatewayProblem(["fake-a", "random"], NotReady));
        Assert.NotNull(TournamentRules.GatewayProblem(["fake-a@high"], NotReady));
        Assert.Null(TournamentRules.GatewayProblem(["random"], NotReady));
        Assert.Null(TournamentRules.GatewayProblem(["fake-a", "random"], Listing));
        Assert.Null(TournamentRules.GatewayProblem(["", null], NotReady));
    }

    [Fact]
    public async Task Creating_a_tournament_or_a_benchmark_answers_503_while_copilot_is_not_signed_in()
    {
        using var app = new NotAuthenticatedApp();
        using var client = app.CreateClient();

        var tournament = await client.PostAsJsonAsync("/api/tournaments",
            new CreateTournamentRequest("T", ["fake-a", "fake-b", "random"], 2, 1));
        var benchmark = await client.PostAsJsonAsync("/api/benchmarks",
            new CreateBenchmarkRequest("B", "fake-a", ["fake-b", "random"], 2, 1));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, tournament.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, benchmark.StatusCode);
        Assert.Contains("copilot", await tournament.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<List<TournamentDto>>("/api/tournaments"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<BenchmarkDto>>("/api/benchmarks"))!);
    }

    [Fact]
    public async Task A_game_with_a_null_model_id_on_an_llm_side_is_a_400_not_a_500()
    {
        using var app = new FakeGatewayApp();
        using var client = app.CreateClient();
        var body = new StringContent(
            """{"white":{"kind":"llm","modelId":null},"black":{"kind":"random","modelId":"random"},"rated":false}""",
            System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/games", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tournament_with_a_null_model_is_a_400()
    {
        using var app = new FakeGatewayApp();
        using var client = app.CreateClient();
        var body = new StringContent(
            """{"name":"T","models":["fake-a",null],"gamesPerPair":2,"maxConcurrency":1}""", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/tournaments", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- The estimate comes before any game is saved ----

    [Fact]
    public async Task A_tournament_whose_estimate_fails_leaves_no_run_behind()
    {
        await using var harness = new TournamentHarness(
            [new LlmModelInfo("fake-a", "A", 0, true), new LlmModelInfo("fake-b", "B", 0, true)],
            listFailure: new TimeoutException("The gateway hung."));

        await Assert.ThrowsAsync<TimeoutException>(() => harness.CreateAsync(["fake-a", "fake-b"]));

        await using var db = await harness.Factory.CreateDbContextAsync();
        Assert.Empty(await db.Tournaments.ToListAsync());
        Assert.Empty(await db.Games.ToListAsync());
        Assert.Empty(harness.Runner.Enqueued);
    }

    [Fact]
    public async Task A_benchmark_whose_estimate_fails_leaves_no_run_behind()
    {
        await using var harness = new TournamentHarness(
            [new LlmModelInfo("fake-a", "A", 0, true), new LlmModelInfo("fake-b", "B", 0, true)],
            listFailure: new TimeoutException("The gateway hung."));
        var benchmarks = new BenchmarkService(harness.Factory, harness.Tournaments, harness.Catalog, harness.Costs, harness.Clock);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            benchmarks.CreateAsync(new CreateBenchmarkRequest("B", "fake-a", ["fake-b"], 2, 1), ["fake-b"], CancellationToken.None));

        await using var db = await harness.Factory.CreateDbContextAsync();
        Assert.Empty(await db.Tournaments.ToListAsync());
        Assert.Empty(await db.Games.ToListAsync());
    }

    // ---- The benchmark keeps the versions that played it ----

    private static async Task<(TournamentHarness Harness, BenchmarkService Service, Guid Id)> NewBenchmarkAsync()
    {
        var harness = new TournamentHarness([new LlmModelInfo("fake-a", "A", 0, true), new LlmModelInfo("fake-b", "B", 0, true)]);
        var service = new BenchmarkService(harness.Factory, harness.Tournaments, harness.Catalog, harness.Costs, harness.Clock);
        var created = await service.CreateAsync(new CreateBenchmarkRequest("B", "fake-a", ["fake-b"], 2, 1), ["fake-b"], CancellationToken.None);
        return (harness, service, created.Benchmark.Id);
    }

    private static async Task SetVersionsAsync(TournamentHarness harness, Guid id, string? app, string? formula, string? prompt)
    {
        await using var db = await harness.Factory.CreateDbContextAsync();
        var run = await db.Tournaments.SingleAsync(t => t.Id == id);
        run.AppVersion = app;
        run.FormulaVersion = formula;
        run.PromptVersion = prompt;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_new_benchmark_stores_the_versions_of_the_build_that_creates_it()
    {
        var (harness, service, id) = await NewBenchmarkAsync();
        await using var _ = harness;

        await using var db = await harness.Factory.CreateDbContextAsync();
        var run = await db.Tournaments.SingleAsync(t => t.Id == id);
        Assert.Equal(BenchmarkFingerprint.AppVersion(), run.AppVersion);
        Assert.Equal(RatingSnapshotWriter.FormulaVersion, run.FormulaVersion);
        Assert.Equal(BenchmarkFingerprint.PromptVersion("fake-a"), run.PromptVersion);
    }

    [Fact]
    public async Task The_report_shows_the_stored_versions_not_those_of_the_build_that_renders_it()
    {
        var (harness, service, id) = await NewBenchmarkAsync();
        await using var _ = harness;
        await SetVersionsAsync(harness, id, "0.0.1-old+abc", "f-old", "p-old");

        var report = await service.ReportAsync(id, CancellationToken.None);

        Assert.Equal("0.0.1-old+abc", report!.Fingerprint.AppVersion);
        Assert.Equal("f-old", report.Fingerprint.FormulaVersion);
        Assert.Equal("p-old", report.Fingerprint.PromptVersion);
        Assert.Equal(harness.Clock.Now, report.Fingerprint.GeneratedAt);
    }

    [Fact]
    public async Task A_benchmark_from_before_the_versions_were_stored_reports_the_current_ones()
    {
        var (harness, service, id) = await NewBenchmarkAsync();
        await using var _ = harness;
        await SetVersionsAsync(harness, id, null, null, null);

        var report = await service.ReportAsync(id, CancellationToken.None);

        Assert.Equal(BenchmarkFingerprint.AppVersion(), report!.Fingerprint.AppVersion);
        Assert.Equal(RatingSnapshotWriter.FormulaVersion, report.Fingerprint.FormulaVersion);
        Assert.Equal(BenchmarkFingerprint.PromptVersion("fake-a"), report.Fingerprint.PromptVersion);
    }

    // ---- The attempts index ----

    [Fact]
    public void The_attempt_log_order_has_an_index()
    {
        using var db = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var entity = db.Model.FindEntityType(typeof(MoveAttemptEntity))!;

        Assert.Contains(entity.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(MoveAttemptEntity.CreatedAt), nameof(MoveAttemptEntity.GameId),
                nameof(MoveAttemptEntity.Ply), nameof(MoveAttemptEntity.Number)]));
    }
}
