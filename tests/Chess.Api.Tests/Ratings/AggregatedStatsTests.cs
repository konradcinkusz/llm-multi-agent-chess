using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Ratings;
using Chess.Api.Results;
using Chess.Api.Tests.Results;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Ratings;

/// <summary>
/// The move statistics are summed by the database. On a generated set (several models, both colours, random sides, missing
/// annotations and token counts) they must equal what a row-by-row reading of the same moves gives.
/// </summary>
public class AggregatedStatsTests
{
    private static readonly string[] Models = ["random", "m-a", "m-b", "m-c"];

    private static (DbContextOptions<ArenaDbContext> Options, List<GameEntity> Games, List<MoveEntity> Moves) Generate()
    {
        var random = new Random(99);
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var games = new List<GameEntity>();
        var moves = new List<MoveEntity>();
        string[] results = ["1-0", "0-1", "1/2-1/2"];
        for (var i = 0; i < 60; i++)
        {
            var white = Models[random.Next(Models.Length)];
            var black = Models[random.Next(Models.Length)];
            if (white == black)
            {
                black = Models[(Array.IndexOf(Models, white) + 1) % Models.Length];
            }

            var game = RatingSeed.Game(Guid.NewGuid(), white, black, results[random.Next(3)]);
            game.Rated = i % 7 != 0;
            games.Add(game);
            for (var ply = 1; ply <= 2 + random.Next(8); ply++)
            {
                var fen = ply % 2 == 1 ? ResultsFixtures.AfterWhiteMove : ResultsFixtures.AfterBlackMove;
                moves.Add(new MoveEntity
                {
                    GameId = game.Id,
                    Ply = ply,
                    Color = ply % 2 == 1 ? "white" : "black",
                    San = "e4",
                    FenAfter = fen,
                    Attempts = 1 + random.Next(3) / 2,
                    LatencyMs = 50 + random.Next(500),
                    InputTokens = random.Next(4) == 0 ? null : 100 + random.Next(900),
                    OutputTokens = random.Next(4) == 0 ? null : 10 + random.Next(90),
                    CentipawnLoss = random.Next(3) == 0 ? null : random.Next(300),
                    PlayedAt = RatingSeed.T0,
                });
            }
        }

        using var db = new ArenaDbContext(options);
        db.Games.AddRange(games);
        db.Moves.AddRange(moves);
        db.SaveChanges();
        return (options, games, moves);
    }

    private static string ModelOf(string kind, string model) => kind == "random" ? "random" : model;

    [Fact]
    public async Task The_loader_equals_a_row_by_row_reading()
    {
        var (options, games, moves) = Generate();
        var rated = games.Where(g => g.Rated).ToDictionary(g => g.Id);

        var loaded = await RatedGameLoader.LoadAsync(new RatingSeed.OptionsFactory(options), null, CancellationToken.None);

        var expectedOutcomes = rated.Values.OrderBy(g => g.Id).Select(g => new GameOutcome(
            ModelOf(g.WhiteKind, g.WhiteModel), ModelOf(g.BlackKind, g.BlackModel), g.Result == "1-0" ? 1 : g.Result == "0-1" ? 0 : 0.5));
        Assert.Equal(expectedOutcomes, loaded.Outcomes);

        var expectedStats = moves
            .Where(m => rated.ContainsKey(m.GameId))
            .GroupBy(m => m.Color == "white"
                ? ModelOf(rated[m.GameId].WhiteKind, rated[m.GameId].WhiteModel)
                : ModelOf(rated[m.GameId].BlackKind, rated[m.GameId].BlackModel))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PlayerMoveStats(
                g.Key, g.Count(), g.Count(m => m.Attempts == 1),
                g.All(m => m.CentipawnLoss is not null) ? g.Average(m => (double)m.CentipawnLoss!.Value) : null))
            .ToList();
        Assert.Equal(expectedStats.Count, loaded.MoveStats.Count);
        foreach (var (expected, actual) in expectedStats.Zip(loaded.MoveStats))
        {
            Assert.Equal(expected, actual);
        }

        // At least one model has an unknown ACPL and one a known one, so both branches were exercised.
        Assert.Contains(loaded.MoveStats, s => s.AverageCentipawnLoss is null);
    }

    [Fact]
    public async Task The_breakdown_move_columns_equal_a_row_by_row_reading()
    {
        var (options, games, moves) = Generate();
        var decided = games.ToDictionary(g => g.Id);   // every generated game has a result and a finish time

        await using var db = new ArenaDbContext(options);
        var rows = await ResultsBreakdown.ReadAsync(db, null, [], 0.01, CancellationToken.None);

        foreach (var row in rows)
        {
            var mine = moves.Where(m => (m.Color == "white" ? decided[m.GameId].WhiteModel : decided[m.GameId].BlackModel) == row.Model).ToList();
            Assert.Equal(mine.Count, row.Moves);
            Assert.Equal(mine.Count == 0 ? 0 : (double)mine.Count(m => m.Attempts == 1) / mine.Count, row.FirstAttemptLegalRate);
            Assert.Equal(mine.Count == 0 ? 0 : (double)mine.Sum(m => m.LatencyMs) / mine.Count, row.AvgLatencyMs);
            Assert.Equal(Average(mine.Select(m => m.InputTokens)), row.AvgInputTokens);
            Assert.Equal(Average(mine.Select(m => m.OutputTokens)), row.AvgOutputTokens);
            Assert.Equal(Average(mine.Select(m => m.CentipawnLoss)), row.AverageCentipawnLoss);
        }

        Assert.Equal(Models.Length, rows.Count);
    }

    private static double? Average(IEnumerable<int?> values)
    {
        var present = values.Where(v => v is not null).Select(v => (long)v!.Value).ToList();
        return present.Count == 0 ? null : (double)present.Sum() / present.Count;
    }

    [Fact]
    public async Task A_move_with_an_unknown_colour_is_rejected_not_attributed()
    {
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var game = RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", "1-0");
        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(game);
            db.Moves.Add(new MoveEntity { GameId = game.Id, Ply = 1, Color = "", San = "e4", FenAfter = "", Attempts = 1, PlayedAt = RatingSeed.T0 });
            db.SaveChanges();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RatedGameLoader.LoadAsync(new RatingSeed.OptionsFactory(options), null, CancellationToken.None));
    }

    [Fact]
    public async Task A_game_that_has_moves_but_is_decided_later_adds_no_phantom_model_to_the_breakdown()
    {
        // Moves of a still-running game (result *) are not in the breakdown, and no row appears for them.
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var running = RatingSeed.Game(Guid.NewGuid(), "only-moves-a", "only-moves-b", "*");
        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(running);
            db.Moves.Add(new MoveEntity { GameId = running.Id, Ply = 1, Color = "white", San = "e4", FenAfter = ResultsFixtures.AfterWhiteMove, Attempts = 1, PlayedAt = RatingSeed.T0 });
            db.SaveChanges();
        }

        await using var read = new ArenaDbContext(options);
        Assert.Empty(await ResultsBreakdown.ReadAsync(read, null, [], 0.01, CancellationToken.None));
    }
}
