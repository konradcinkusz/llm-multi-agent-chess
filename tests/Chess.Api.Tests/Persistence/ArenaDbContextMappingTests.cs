using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Persistence;

public class ArenaDbContextMappingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static ArenaDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);

    [Fact]
    public async Task Game_round_trips_every_column()
    {
        var name = Guid.NewGuid().ToString();
        var game = new GameEntity
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            WhiteKind = "llm",
            WhiteModel = "gpt-x",
            BlackKind = "random",
            BlackModel = "random",
            Rated = true,
            Result = "1-0",
            Termination = "checkmate",
            StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            CurrentFen = "8/8/8/8/8/8/8/K6k w - - 0 1",
            PlyCount = 42,
            Pgn = "1. e4 e5 1-0",
            CreatedAt = T0,
            StartedAt = T0.AddMinutes(1),
            FinishedAt = T0.AddMinutes(2),
        };
        await using (var write = NewContext(name))
        {
            write.Games.Add(game);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.Games.SingleAsync(g => g.Id == game.Id);

        Assert.Equal(game.TournamentId, loaded.TournamentId);
        Assert.Equal("llm", loaded.WhiteKind);
        Assert.Equal("gpt-x", loaded.WhiteModel);
        Assert.Equal("random", loaded.BlackKind);
        Assert.Equal("random", loaded.BlackModel);
        Assert.True(loaded.Rated);
        Assert.Equal("1-0", loaded.Result);
        Assert.Equal("checkmate", loaded.Termination);
        Assert.Equal(game.StartFen, loaded.StartFen);
        Assert.Equal(game.CurrentFen, loaded.CurrentFen);
        Assert.Equal(42, loaded.PlyCount);
        Assert.Equal("1. e4 e5 1-0", loaded.Pgn);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddMinutes(1), loaded.StartedAt);
        Assert.Equal(T0.AddMinutes(2), loaded.FinishedAt);
    }

    [Fact]
    public async Task Move_round_trips_and_ply_is_unique_per_game()
    {
        var name = Guid.NewGuid().ToString();
        var gameId = Guid.NewGuid();
        await using (var write = NewContext(name))
        {
            write.Moves.Add(new MoveEntity
            {
                GameId = gameId, Ply = 1, Color = "white", San = "e4", FenAfter = "fen1",
                Attempts = 2, LatencyMs = 1234, InputTokens = 500, OutputTokens = 7,
                CentipawnLoss = 12, BestSan = "d4", PlayedAt = T0,
            });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.Moves.SingleAsync(m => m.GameId == gameId && m.Ply == 1);

        Assert.Equal("white", loaded.Color);
        Assert.Equal("e4", loaded.San);
        Assert.Equal("fen1", loaded.FenAfter);
        Assert.Equal(2, loaded.Attempts);
        Assert.Equal(1234L, loaded.LatencyMs);
        Assert.Equal(500, loaded.InputTokens);
        Assert.Equal(7, loaded.OutputTokens);
        Assert.Equal(12, loaded.CentipawnLoss);
        Assert.Equal("d4", loaded.BestSan);
        Assert.Equal(T0, loaded.PlayedAt);
    }

    [Fact]
    public async Task Move_attempt_round_trips_including_nullable_columns()
    {
        var name = Guid.NewGuid().ToString();
        var gameId = Guid.NewGuid();
        await using (var write = NewContext(name))
        {
            write.MoveAttempts.Add(new MoveAttemptEntity
            {
                GameId = gameId, Ply = 3, Number = 2, Prompt = "prompt text", RawReply = "I play Nf3.",
                ParsedSan = null, Legal = false, LatencyMs = 900, Error = "illegal", CreatedAt = T0,
            });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.MoveAttempts.SingleAsync(a => a.GameId == gameId && a.Ply == 3 && a.Number == 2);

        Assert.Equal("prompt text", loaded.Prompt);
        Assert.Equal("I play Nf3.", loaded.RawReply);
        Assert.Null(loaded.ParsedSan);
        Assert.False(loaded.Legal);
        Assert.Equal(900L, loaded.LatencyMs);
        Assert.Equal("illegal", loaded.Error);
        Assert.Equal(T0, loaded.CreatedAt);
    }

    [Fact]
    public async Task Tournament_round_trips_models_as_a_list()
    {
        var name = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        await using (var write = NewContext(name))
        {
            write.Tournaments.Add(new TournamentEntity
            {
                Id = id, Name = "Spring", Models = ["fake-a", "fake-b", "random"], GamesPerPair = 2,
                MaxConcurrency = 2, Status = "running", CreatedAt = T0, FinishedAt = null,
            });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.Tournaments.SingleAsync(t => t.Id == id);

        Assert.Equal("Spring", loaded.Name);
        Assert.Equal(new[] { "fake-a", "fake-b", "random" }, loaded.Models);
        Assert.Equal(2, loaded.GamesPerPair);
        Assert.Equal(2, loaded.MaxConcurrency);
        Assert.Equal("running", loaded.Status);
        Assert.Null(loaded.FinishedAt);
    }

    [Fact]
    public async Task Tournament_models_change_is_detected_by_the_value_comparer()
    {
        var name = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        await using (var write = NewContext(name))
        {
            write.Tournaments.Add(new TournamentEntity { Id = id, Name = "T", Models = ["a"] });
            await write.SaveChangesAsync();
        }

        await using var update = NewContext(name);
        var tournament = await update.Tournaments.SingleAsync(t => t.Id == id);
        tournament.Models.Add("b");
        await update.SaveChangesAsync();

        await using var read = NewContext(name);
        Assert.Equal(new[] { "a", "b" }, (await read.Tournaments.SingleAsync(t => t.Id == id)).Models);
    }

    [Fact]
    public async Task Rating_snapshot_round_trips_with_generated_id()
    {
        var name = Guid.NewGuid().ToString();
        var afterGame = Guid.NewGuid();
        await using (var write = NewContext(name))
        {
            write.Games.Add(new GameEntity { Id = afterGame, CreatedAt = T0 });
            write.RatingSnapshots.Add(new RatingSnapshotEntity
            {
                TakenAt = T0, AfterGameId = afterGame, RatedGames = 3, Model = "fake-a", Rating = 1510.5,
                CiLow = 1400.25, CiHigh = 1620.75, ArenaScore = 0.625, Games = 3, Provisional = true,
                FormulaVersion = "1",
            });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.RatingSnapshots.SingleAsync();

        Assert.True(loaded.Id > 0);
        Assert.Equal(T0, loaded.TakenAt);
        Assert.Equal(afterGame, loaded.AfterGameId);
        Assert.Equal(3, loaded.RatedGames);
        Assert.Equal("fake-a", loaded.Model);
        Assert.Equal(1510.5, loaded.Rating);
        Assert.Equal(1400.25, loaded.CiLow);
        Assert.Equal(1620.75, loaded.CiHigh);
        Assert.Equal(0.625, loaded.ArenaScore);
        Assert.Equal(3, loaded.Games);
        Assert.True(loaded.Provisional);
        Assert.Equal("1", loaded.FormulaVersion);
    }

    [Fact]
    public void Model_maps_every_table_with_snake_case_columns()
    {
        using var context = NewContext(Guid.NewGuid().ToString());
        var entities = context.Model.GetEntityTypes().ToList();

        Assert.Equal(
            new[]
            {
                "broadcast_schedules", "broadcast_slots", "games", "move_attempts", "moves", "rating_snapshots", "tournaments",
            },
            entities.Select(e => e.GetTableName()).OrderBy(n => n).ToArray());

        var columns = entities.SelectMany(e => e.GetProperties()).Select(p => p.GetColumnName()).ToList();
        Assert.Contains("white_kind", columns);
        Assert.Contains("ply_count", columns);
        Assert.Contains("latency_ms", columns);
        Assert.Contains("ci_low", columns);
        Assert.Contains("formula_version", columns);
        Assert.All(columns, c => Assert.Equal(c.ToLowerInvariant(), c));
    }
}
