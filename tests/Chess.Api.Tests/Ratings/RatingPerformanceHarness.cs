using System.Diagnostics;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Persistence.Entities;
using Xunit;
using Xunit.Abstractions;
using static Chess.Api.Tests.Results.ResultsFixtures;

namespace Chess.Api.Tests.Ratings;

/// <summary>
/// Times the rating and results endpoints over a generated archive: one random player and eight models, every pairing,
/// about 60 moves a game, an attempt per LLM move and a rating snapshot per game and model. In-process over the HTTP
/// pipeline on the InMemory provider (no network, no server). It measures a cold request, then a warm one.
/// <para>
/// By default it runs a small set and checks the answers are sane. For the figures in the pull request set
/// <c>ARENA_PERF_GAMES=808</c> and run with
/// <c>dotnet test tests/Chess.Api.Tests --filter FullyQualifiedName~RatingPerformanceHarness --logger "console;verbosity=detailed"</c>;
/// the same file runs unchanged on the code before the change, because it only calls the HTTP endpoints.
/// </para>
/// </summary>
public sealed class RatingPerformanceHarness(ITestOutputHelper output)
{
    private static readonly string[] Models = ["random", "m1", "m2", "m3", "m4", "m5", "m6", "m7", "m8"];

    private static int GameCount =>
        int.TryParse(Environment.GetEnvironmentVariable("ARENA_PERF_GAMES"), out var n) && n > 0 ? n : 120;

    [Fact]
    public async Task Endpoints_answer_over_a_generated_archive()
    {
        using var app = new RatingsApp();
        var games = GameCount;
        var seeding = Stopwatch.StartNew();
        app.Seed(db => Generate(db, games));
        output.WriteLine($"seeded {games} rated games, {Models.Length} players in {seeding.ElapsedMilliseconds} ms");
        var client = app.CreateClient();

        var board = await Time<LeaderboardDto>(client, "/api/ratings");
        var matrix = await Time<MatrixDto>(client, "/api/ratings/matrix");
        var pair = await Time<Chess.Rating.PairwiseCoefficient>(client, $"/api/ratings/pairwise?a={board.Models[0].Model}&b={board.Models[1].Model}");
        var breakdown = await Time<List<ModelResultsDto>>(client, "/api/results/breakdown");
        var history = await Time<List<RatingHistoryPointDto>>(client, "/api/ratings/history");

        Assert.Equal(games, board.Games);
        Assert.Equal(Models.Length * (Models.Length - 1), matrix.Cells.Count);
        Assert.Equal(pair, matrix.Cells.Single(c => c.A == pair.A && c.B == pair.B));
        Assert.Equal(Models.Length, breakdown.Count);
        Assert.NotEmpty(history);
    }

    private async Task<T> Time<T>(HttpClient client, string url)
    {
        var cold = Stopwatch.StartNew();
        using var first = await client.GetAsync(url);
        var bytes = (await first.Content.ReadAsByteArrayAsync()).Length;
        var coldMs = cold.ElapsedMilliseconds;
        first.EnsureSuccessStatusCode();

        var warm = Stopwatch.StartNew();
        var result = await client.GetFromJsonAsync<T>(url);
        output.WriteLine($"{url,-52} cold {coldMs,6} ms   warm {warm.ElapsedMilliseconds,6} ms   {bytes / 1024.0,8:0.0} KiB");
        return result!;
    }

    private static void Generate(Chess.Api.Persistence.ArenaDbContext db, int count)
    {
        var random = new Random(2026);
        string[] results = ["1-0", "0-1", "1/2-1/2"];
        var snapshots = new List<RatingSnapshotEntity>();
        for (var n = 1; n <= count; n++)
        {
            var white = Models[random.Next(Models.Length)];
            var black = Models[random.Next(Models.Length)];
            while (black == white)
            {
                black = Models[random.Next(Models.Length)];
            }

            var game = RatingSeed.Game(Guid.NewGuid(), white, black, results[random.Next(3)]);
            game.FinishedAt = T0.AddMinutes(n);
            game.Termination = "checkmate";
            db.Games.Add(game);

            var plies = 40 + random.Next(40);
            for (var ply = 1; ply <= plies; ply++)
            {
                var whiteMoved = ply % 2 == 1;
                var llm = (whiteMoved ? white : black) != "random";
                var attempts = llm ? 1 + (random.Next(10) == 0 ? 1 : 0) : 1;
                db.Moves.Add(new MoveEntity
                {
                    GameId = game.Id,
                    Ply = ply,
                    Color = whiteMoved ? "white" : "black",
                    San = "e4",
                    FenAfter = whiteMoved ? AfterWhiteMove : AfterBlackMove,
                    Attempts = attempts,
                    LatencyMs = 200 + random.Next(2000),
                    InputTokens = llm ? 800 + random.Next(400) : null,
                    OutputTokens = llm ? 20 + random.Next(80) : null,
                    CentipawnLoss = random.Next(10) < 7 ? random.Next(200) : null,
                    PlayedAt = T0,
                });
                for (var number = 1; llm && number <= attempts; number++)
                {
                    db.MoveAttempts.Add(new MoveAttemptEntity
                    {
                        GameId = game.Id,
                        Ply = ply,
                        Number = number,
                        Prompt = "",
                        Legal = number == attempts,
                        LatencyMs = 100,
                        CreatedAt = T0,
                        InputTokens = 800 + random.Next(400),
                        OutputTokens = 20 + random.Next(80),
                    });
                }
            }

            foreach (var model in Models)
            {
                snapshots.Add(Snapshot(game.Id, T0.AddMinutes(n), n, model, random.Next(-100, 300)));
            }
        }

        db.RatingSnapshots.AddRange(snapshots);
    }
}
