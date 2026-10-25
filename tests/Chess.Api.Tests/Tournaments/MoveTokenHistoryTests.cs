using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Tournaments;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

/// <summary>The per-model token averages of the cost estimates, read from the stored moves (03 §10.2).</summary>
public class MoveTokenHistoryTests
{
    [Fact]
    public async Task Averages_each_model_over_the_moves_it_made_on_either_side()
    {
        await using var db = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var game = Game(white: "fake-a", black: "fake-b");
        db.Games.Add(game);
        db.Moves.AddRange(
            Move(game, 1, "white", input: 100, output: 10),
            Move(game, 2, "black", input: 200, output: 20),
            Move(game, 3, "white", input: 300, output: 30),
            // A move that reported no tokens does not count towards the average.
            Move(game, 4, "black", input: null, output: null));
        await db.SaveChangesAsync();

        var averages = await MoveTokenHistory.ReadAsync(db, CancellationToken.None);

        Assert.Equal(new ModelTokenAverage(Moves: 2, AverageInputTokens: 200, AverageOutputTokens: 20), averages["fake-a"]);
        Assert.Equal(new ModelTokenAverage(Moves: 1, AverageInputTokens: 200, AverageOutputTokens: 20), averages["fake-b"]);
    }

    [Fact]
    public async Task A_model_with_no_stored_moves_is_absent_so_its_estimate_is_assumed()
    {
        await using var db = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var game = Game(white: "fake-a", black: "random");
        db.Games.Add(game);
        // The random player reports no tokens, so its move is not averaged and no model is listed.
        db.Moves.Add(Move(game, 1, "black", input: null, output: null));
        await db.SaveChangesAsync();

        var averages = await MoveTokenHistory.ReadAsync(db, CancellationToken.None);

        Assert.Empty(averages);
    }

    private static GameEntity Game(string white, string black) => new()
    {
        Id = Guid.NewGuid(),
        WhiteKind = white == "random" ? "random" : "llm",
        WhiteModel = white,
        BlackKind = black == "random" ? "random" : "llm",
        BlackModel = black,
        Rated = true,
        Result = "*",
        StartFen = "start",
        CurrentFen = "start",
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static MoveEntity Move(GameEntity game, int ply, string color, int? input, int? output) => new()
    {
        GameId = game.Id,
        Ply = ply,
        Color = color,
        San = "e4",
        FenAfter = "fen",
        Attempts = 1,
        LatencyMs = 1000,
        InputTokens = input,
        OutputTokens = output,
        PlayedAt = DateTimeOffset.UnixEpoch,
    };
}
