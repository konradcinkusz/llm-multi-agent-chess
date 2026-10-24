using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Results;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Chess.Api.Tests.Results.ResultsFixtures;

namespace Chess.Api.Tests.Results;

public class RatingSnapshotWriterTests
{
    private static List<string> ModelsAfter(IEnumerable<RatingSnapshotEntity> rows, Guid gameId) => rows
        .Where(r => r.AfterGameId == gameId)
        .Select(r => r.Model)
        .Order(StringComparer.Ordinal)
        .ToList();

    [Fact]
    public async Task Three_finished_rated_games_write_three_batches_in_finish_order()
    {
        var name = Guid.NewGuid().ToString();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            // Inserted out of finish order: the finish time, not the insert order, decides the batches.
            seed.Games.Add(Game(third, "B", "A", "0-1", "checkmate", T0.AddMinutes(3)));
            seed.Games.Add(Game(first, "A", "random", "1-0", "checkmate", T0.AddMinutes(1)));
            seed.Games.Add(Game(second, "random", "A", "1/2-1/2", "stalemate", T0.AddMinutes(2)));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var batches = await RatingSnapshotWriter.WriteNewAsync(db, new StepClock(T0), CancellationToken.None);

        Assert.Equal(3, batches);
        var rows = await db.RatingSnapshots.OrderBy(s => s.Id).ToListAsync();
        // Batch sizes follow the number of models rated so far: {A, random}, {A, random}, {A, B, random}.
        Assert.Equal(
            new[] { first, first, second, second, third, third, third },
            rows.Select(r => r.AfterGameId).ToArray());
        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3, 3 }, rows.Select(r => r.RatedGames).ToArray());
        Assert.Equal(["A", "random"], ModelsAfter(rows, first));
        Assert.Equal(["A", "random"], ModelsAfter(rows, second));
        Assert.Equal(["A", "B", "random"], ModelsAfter(rows, third));
        Assert.All(rows, r => Assert.Equal("1", r.FormulaVersion));

        // A has played all three games by the last batch.
        Assert.Equal(3, rows.Single(r => r.AfterGameId == third && r.Model == "A").Games);
        Assert.Equal(new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) }, rows.Select(r => r.TakenAt).Distinct().ToArray());
    }

    [Fact]
    public async Task A_restart_resumes_after_the_newest_snapshot_and_writes_nothing_twice()
    {
        var name = Guid.NewGuid().ToString();
        var games = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using (var seed = NewContext(name))
        {
            seed.Games.AddRange(
                Game(games[0], "A", "random", "1-0", "checkmate", T0.AddMinutes(1)),
                Game(games[1], "random", "A", "1/2-1/2", "stalemate", T0.AddMinutes(2)),
                Game(games[2], "B", "A", "0-1", "checkmate", T0.AddMinutes(3)));
            await seed.SaveChangesAsync();
        }

        await using (var before = NewContext(name))
        {
            Assert.Equal(3, await RatingSnapshotWriter.WriteNewAsync(before, new StepClock(T0), CancellationToken.None));
        }

        // A fresh context stands in for a restarted process.
        await using var restarted = NewContext(name);
        Assert.Equal(0, await RatingSnapshotWriter.WriteNewAsync(restarted, new StepClock(T0), CancellationToken.None));
        Assert.Equal(7, await restarted.RatingSnapshots.CountAsync());
    }

    [Fact]
    public async Task A_game_finished_after_a_restart_is_the_only_one_appended()
    {
        var name = Guid.NewGuid().ToString();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            seed.Games.Add(Game(first, "A", "random", "1-0", "checkmate", T0.AddMinutes(1)));
            await seed.SaveChangesAsync();
            Assert.Equal(1, await RatingSnapshotWriter.WriteNewAsync(seed, new StepClock(T0), CancellationToken.None));
        }

        await using (var later = NewContext(name))
        {
            later.Games.Add(Game(second, "B", "A", "0-1", "checkmate", T0.AddMinutes(2)));
            await later.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        Assert.Equal(1, await RatingSnapshotWriter.WriteNewAsync(db, new StepClock(T0.AddHours(1)), CancellationToken.None));
        // The first batch wrote {A, random}; the second writes {A, B, random} for two rated games.
        var rows = await db.RatingSnapshots.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.Equal(first, rows[0].AfterGameId);
        Assert.Equal(second, rows[2].AfterGameId);
        Assert.All(rows.Skip(2), r => Assert.Equal(2, r.RatedGames));
        Assert.Equal(["A", "B", "random"], ModelsAfter(rows, second));
    }

    [Fact]
    public async Task Unrated_unfinished_and_undecided_games_get_no_snapshot()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = NewContext(name))
        {
            seed.Games.AddRange(
                // Finished and decided, but not rated.
                Game(Guid.NewGuid(), "A", "random", "1-0", "checkmate", T0.AddMinutes(1), rated: false),
                // Started but never finished (for example, cut off by a restart).
                new GameEntity { Id = Guid.NewGuid(), WhiteKind = "llm", WhiteModel = "A", BlackKind = "random", BlackModel = "random", Rated = true, Result = "*", CreatedAt = T0, StartedAt = T0 },
                // Closed without a decided result (aborted or interrupted).
                Game(Guid.NewGuid(), "A", "random", "*", "aborted", T0.AddMinutes(2)));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        Assert.Equal(0, await RatingSnapshotWriter.WriteNewAsync(db, new StepClock(T0), CancellationToken.None));
        Assert.Equal(0, await db.RatingSnapshots.CountAsync());
    }
}
