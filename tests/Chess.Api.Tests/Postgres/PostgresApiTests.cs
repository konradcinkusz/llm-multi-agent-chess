using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Benchmarks;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Tournaments;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// The arena on a real PostgreSQL server (CLAUDE.md lesson #78): the host applies every migration, a fake game, a tournament and
/// a benchmark are played to their ends, and every read endpoint answers 200 with content. The InMemory provider cannot show an
/// untranslatable query or a model that drifted from its migrations; these tests can. Skipped without <c>ARENA_TEST_POSTGRES</c>.
/// </summary>
public class PostgresApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");
    private static readonly PlayerSpec Random = new("random", "random");

    [PostgresFact(Timeout = 120_000)]
    public async Task The_host_applies_every_migration_and_reports_the_database_healthy()
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        await using var db = await app.Factory.CreateDbContextAsync();
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(db.Database.GetMigrations().ToList(), applied);
        Assert.NotEmpty(applied);

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [PostgresFact(Timeout = 300_000)]
    public async Task Every_read_endpoint_answers_200_with_content_after_a_game_a_tournament_and_a_benchmark()
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        // ---- one fake game, played to the end ----
        var game = await CreateGameAsync(client, FakeA, FakeB);
        var finished = await WaitForGameAsync(client, game.Id);
        Assert.Equal("finished", finished.Summary.State);
        Assert.NotEqual("*", finished.Summary.Result);
        Assert.NotEmpty(finished.Moves);

        // ---- a small tournament and a benchmark ----
        var tournament = await CreateTournamentAsync(client);
        var benchmark = await CreateBenchmarkAsync(client);
        await WaitForTournamentAsync(client, tournament.Id);
        var benchmarkDone = await WaitForBenchmarkAsync(client, benchmark.Benchmark.Id);
        Assert.Equal(TournamentStatus.Finished, benchmarkDone.Status);

        // ---- games: paging, filters, ordering ----
        var all = await GetAsync<PageDto<GameSummaryDto>>(client, "/api/games?pageSize=200");
        Assert.True(all.Total >= 1 + 6 + 4, $"expected the game, the tournament's 6 and the benchmark's 4, got {all.Total}");
        Assert.Equal(all.Total, all.Items.Count);
        Assert.Equal(all.Items.OrderByDescending(g => g.CreatedAt).Select(g => g.Id), all.Items.Select(g => g.Id));
        Assert.Contains(all.Items, g => g.TournamentId == tournament.Id);

        var paged = await GetAsync<PageDto<GameSummaryDto>>(client, "/api/games?page=2&pageSize=3");
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal(all.Items.Skip(3).Take(3).Select(g => g.Id), paged.Items.Select(g => g.Id));
        var beyond = await GetAsync<PageDto<GameSummaryDto>>(client, "/api/games?page=999&pageSize=50");
        Assert.Empty(beyond.Items);
        Assert.Equal(all.Total, beyond.Total);

        var byModel = await GetAsync<PageDto<GameSummaryDto>>(client, "/api/games?model=fake-a&pageSize=200");
        Assert.NotEmpty(byModel.Items);
        Assert.All(byModel.Items, g => Assert.True(g.White.ModelId == "fake-a" || g.Black.ModelId == "fake-a"));
        foreach (var state in new[] { "queued", "running", "finished" })
        {
            var byState = await GetAsync<PageDto<GameSummaryDto>>(client, $"/api/games?status={state}");
            Assert.All(byState.Items, g => Assert.Equal(state, g.State));
        }

        var finishedGames = await GetAsync<PageDto<GameSummaryDto>>(client, "/api/games?status=finished&pageSize=200");
        Assert.Equal(all.Total, finishedGames.Total);

        // ---- one game: detail, usage, PGN ----
        var detail = await GetAsync<GameDetailDto>(client, $"/api/games/{game.Id}");
        Assert.Equal(finished.Moves.Count, detail.Moves.Count);
        Assert.Equal(Enumerable.Range(1, detail.Moves.Count), detail.Moves.Select(m => m.Ply));

        var usage = await client.GetAsync($"/api/games/{game.Id}/usage");
        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        Assert.Equal(game.Id, (await usage.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("gameId").GetGuid());

        var pgn = await client.GetAsync($"/api/games/{game.Id}/pgn");
        Assert.Equal(HttpStatusCode.OK, pgn.StatusCode);
        var pgnText = await pgn.Content.ReadAsStringAsync();
        Assert.Contains("[White \"fake-a\"]", pgnText);
        Assert.Contains("[Black \"fake-b\"]", pgnText);
        Assert.Contains("1. ", pgnText);

        // ---- attempts: every filter ----
        var attempts = await GetAsync<PageDto<AttemptDto>>(client, "/api/attempts?pageSize=200");
        Assert.True(attempts.Total > 0);
        Assert.Equal(attempts.Items.OrderByDescending(a => a.CreatedAt).Select(a => (a.GameId, a.Ply, a.Number)).Count(),
            attempts.Items.Count);
        var ofGame = await GetAsync<PageDto<AttemptDto>>(client, $"/api/attempts?gameId={game.Id}&pageSize=200");
        Assert.NotEmpty(ofGame.Items);
        Assert.All(ofGame.Items, a => Assert.Equal(game.Id, a.GameId));
        var ofModel = await GetAsync<PageDto<AttemptDto>>(client, "/api/attempts?model=fake-b&pageSize=200");
        Assert.NotEmpty(ofModel.Items);
        Assert.All(ofModel.Items, a => Assert.Equal("fake-b", a.ModelId));
        var legal = await GetAsync<PageDto<AttemptDto>>(client, "/api/attempts?legal=true&pageSize=200");
        Assert.NotEmpty(legal.Items);
        Assert.All(legal.Items, a => Assert.True(a.Legal));
        var illegal = await GetAsync<PageDto<AttemptDto>>(client, "/api/attempts?legal=false&pageSize=200");
        Assert.All(illegal.Items, a => Assert.False(a.Legal));
        Assert.Equal(attempts.Total, legal.Total + illegal.Total);
        var combined = await GetAsync<PageDto<AttemptDto>>(
            client, $"/api/attempts?gameId={game.Id}&model=fake-a&legal=true&page=1&pageSize=2");
        Assert.True(combined.Items.Count <= 2);
        Assert.All(combined.Items, a => Assert.Equal("fake-a", a.ModelId));
        var pageTwo = await GetAsync<PageDto<AttemptDto>>(client, "/api/attempts?page=2&pageSize=5");
        Assert.Equal(attempts.Items.Skip(5).Take(5).Select(a => (a.GameId, a.Ply, a.Number)),
            pageTwo.Items.Select(a => (a.GameId, a.Ply, a.Number)));

        // ---- ratings: leaderboard (both orders), matrix, pairwise, history, breakdown ----
        var board = await GetAsync<JsonElement>(client, "/api/ratings");
        var models = board.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("model").GetString()).ToList();
        Assert.Contains("fake-a", models);
        Assert.Contains("fake-b", models);
        Assert.Contains("random", models);
        Assert.Equal(all.Total, board.GetProperty("games").GetInt32());
        var conservative = await GetAsync<JsonElement>(client, "/api/ratings?order=conservative");
        Assert.Equal(models.Count, conservative.GetProperty("models").GetArrayLength());
        var ofTournament = await GetAsync<JsonElement>(client, $"/api/ratings?tournamentId={tournament.Id}");
        Assert.Equal(6, ofTournament.GetProperty("games").GetInt32());

        var matrix = await GetAsync<JsonElement>(client, "/api/ratings/matrix");
        Assert.Equal(models.Count * (models.Count - 1), matrix.GetProperty("cells").GetArrayLength());
        var matrixOfTournament = await GetAsync<JsonElement>(client, $"/api/ratings/matrix?tournamentId={tournament.Id}");
        Assert.Equal(3, matrixOfTournament.GetProperty("models").GetArrayLength());

        var pairwise = await client.GetAsync("/api/ratings/pairwise?a=fake-a&b=fake-b");
        Assert.Equal(HttpStatusCode.OK, pairwise.StatusCode);
        Assert.NotEqual(JsonValueKind.Undefined, (await pairwise.Content.ReadFromJsonAsync<JsonElement>(Json)).ValueKind);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/ratings/pairwise?a=fake-b&b=random&tournamentId={tournament.Id}")).StatusCode);

        var breakdown = await GetAsync<JsonElement>(client, "/api/results/breakdown");
        Assert.NotEqual(JsonValueKind.Undefined, breakdown.ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/results/breakdown?tournamentId={tournament.Id}")).StatusCode);

        // The snapshot service writes on its own clock (RatingSnapshots:Interval is one second in this app).
        var history = await WaitForHistoryAsync(client);
        Assert.All(history, p => Assert.False(string.IsNullOrEmpty(p.Model)));
        var historyOfModel = await GetAsync<List<RatingHistoryPointDto>>(client, "/api/ratings/history?model=fake-a");
        Assert.NotEmpty(historyOfModel);
        Assert.All(historyOfModel, p => Assert.Equal("fake-a", p.Model));

        // ---- tournaments and benchmarks ----
        var tournaments = await GetAsync<List<TournamentDto>>(client, "/api/tournaments");
        Assert.Contains(tournaments, t => t.Id == tournament.Id && t.Status == TournamentStatus.Finished && t.GamesFinished == 6);
        Assert.DoesNotContain(tournaments, t => t.Id == benchmark.Benchmark.Id);
        var oneTournament = await GetAsync<TournamentDto>(client, $"/api/tournaments/{tournament.Id}");
        Assert.Equal(6, oneTournament.GamesTotal);

        var benchmarks = await GetAsync<List<BenchmarkDto>>(client, "/api/benchmarks");
        Assert.Contains(benchmarks, b => b.Id == benchmark.Benchmark.Id);
        var oneBenchmark = await GetAsync<BenchmarkDto>(client, $"/api/benchmarks/{benchmark.Benchmark.Id}");
        Assert.Equal(4, oneBenchmark.GamesFinished);
        var report = await GetAsync<BenchmarkReportDto>(client, $"/api/benchmarks/{benchmark.Benchmark.Id}/report");
        Assert.Equal(benchmark.Benchmark.Id, report.Benchmark.Id);
        Assert.Equal(2, report.Opponents.Count);
        var markdown = await client.GetAsync($"/api/benchmarks/{benchmark.Benchmark.Id}/report?format=md");
        Assert.Equal(HttpStatusCode.OK, markdown.StatusCode);
        Assert.Contains("fake-b", await markdown.Content.ReadAsStringAsync());

        // ---- the rest of the read surface ----
        foreach (var path in new[]
                 {
                     "/api/status", "/api/models", "/api/prompt-styles", "/api/cost/inputs", "/api/results/archive/status",
                     "/api/broadcast/now", "/api/broadcast/schedules", "/api/requests/queue", "/api/requests/menu",
                 })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
        }
    }

    // ---- helpers ----

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<GameSummaryDto> CreateGameAsync(HttpClient client, PlayerSpec white, PlayerSpec black)
    {
        var response = await client.PostAsJsonAsync("/api/games", new CreateGameRequest(white, black, Rated: true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GameSummaryDto>(Json))!;
    }

    private static async Task<GameDetailDto> WaitForGameAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (true)
        {
            var detail = await GetAsync<GameDetailDto>(client, $"/api/games/{id}");
            if (detail.Summary.State == "finished" || DateTime.UtcNow > deadline)
            {
                return detail;
            }

            await Task.Delay(100);
        }
    }

    private static async Task<TournamentDto> CreateTournamentAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments",
            new CreateTournamentRequest("Postgres cup", ["fake-a", "fake-b", "random"], GamesPerPair: 2, MaxConcurrency: 2));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TournamentCreatedDto>(Json))!.Tournament;
    }

    private static async Task<BenchmarkCreatedDto> CreateBenchmarkAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/benchmarks",
            new CreateBenchmarkRequest("Postgres pick", "fake-a@high", ["fake-b", "random"], GamesPerOpponent: 2, MaxConcurrency: 2));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BenchmarkCreatedDto>(Json))!;
    }

    private static async Task WaitForTournamentAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (true)
        {
            var tournament = await GetAsync<TournamentDto>(client, $"/api/tournaments/{id}");
            if (tournament.Status != TournamentStatus.Running)
            {
                Assert.Equal(TournamentStatus.Finished, tournament.Status);
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "the tournament did not finish in time");
            await Task.Delay(200);
        }
    }

    private static async Task<BenchmarkDto> WaitForBenchmarkAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (true)
        {
            var benchmark = await GetAsync<BenchmarkDto>(client, $"/api/benchmarks/{id}");
            if (benchmark.Status != TournamentStatus.Running)
            {
                return benchmark;
            }

            Assert.True(DateTime.UtcNow < deadline, "the benchmark did not finish in time");
            await Task.Delay(200);
        }
    }

    private static async Task<List<RatingHistoryPointDto>> WaitForHistoryAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            var history = await GetAsync<List<RatingHistoryPointDto>>(client, "/api/ratings/history");
            if (history.Count > 0)
            {
                return history;
            }

            Assert.True(DateTime.UtcNow < deadline, "no rating snapshot was written");
            await Task.Delay(500);
        }
    }
}
