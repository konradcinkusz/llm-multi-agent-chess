using Chess.Api.Results;
using Xunit;
using static Chess.Api.Tests.Results.ResultsFixtures;

namespace Chess.Api.Tests.Results;

public class RatingHistoryCapTests
{
    private static async Task<(string Name, Guid Game)> SeedAsync(int snapshotsPerModel, params string[] models)
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await using var db = NewContext(name);
        for (var n = 1; n <= snapshotsPerModel; n++)
        {
            foreach (var model in models)
            {
                db.RatingSnapshots.Add(Snapshot(game, T0.AddMinutes(n), n, model, 100 + n));
            }
        }

        await db.SaveChangesAsync();
        return (name, game);
    }

    [Fact]
    public async Task A_long_history_is_thinned_to_the_cap_per_model_and_keeps_both_ends()
    {
        var (name, _) = await SeedAsync(1_000, "A", "B");
        await using var db = NewContext(name);

        var points = await RatingHistory.ReadAsync(db, null, CancellationToken.None);

        foreach (var model in new[] { "A", "B" })
        {
            var mine = points.Where(p => p.Model == model).ToList();
            Assert.InRange(mine.Count, 100, RatingHistory.MaxPointsPerModel + 2);
            Assert.Equal(1, mine[0].RatedGames);              // the first snapshot is kept
            Assert.Equal(1_000, mine[^1].RatedGames);         // so is the latest
            Assert.Equal(mine.OrderBy(p => p.RatedGames).Select(p => p.RatedGames), mine.Select(p => p.RatedGames));   // oldest first
        }

        // Both models sit on the same x positions, so their lines stay comparable.
        Assert.Equal(
            points.Where(p => p.Model == "A").Select(p => p.RatedGames),
            points.Where(p => p.Model == "B").Select(p => p.RatedGames));
    }

    [Fact]
    public async Task A_thinned_point_is_a_stored_snapshot_not_an_average()
    {
        var (name, _) = await SeedAsync(900, "A");
        await using var db = NewContext(name);

        var points = await RatingHistory.ReadAsync(db, "A", CancellationToken.None);

        Assert.All(points, p => Assert.Equal(100 + p.RatedGames, p.Rating));
    }

    [Fact]
    public async Task A_history_within_the_cap_is_returned_whole()
    {
        var (name, _) = await SeedAsync(RatingHistory.MaxPointsPerModel, "A", "B");
        await using var db = NewContext(name);

        var points = await RatingHistory.ReadAsync(db, null, CancellationToken.None);

        Assert.Equal(2 * RatingHistory.MaxPointsPerModel, points.Count);
    }

    [Fact]
    public async Task A_model_that_started_late_keeps_its_own_first_and_latest_points()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            for (var n = 1; n <= 700; n++)
            {
                seed.RatingSnapshots.Add(Snapshot(game, T0.AddMinutes(n), n, "old", 100));
                if (n >= 653)
                {
                    seed.RatingSnapshots.Add(Snapshot(game, T0.AddMinutes(n), n, "new", 50));
                }
            }

            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var mine = (await RatingHistory.ReadAsync(db, "new", CancellationToken.None)).Select(p => p.RatedGames).ToList();

        // The model alone has 48 points, below the cap, so nothing is thinned for it.
        Assert.Equal(Enumerable.Range(653, 48), mine);
        var everyone = (await RatingHistory.ReadAsync(db, null, CancellationToken.None)).Where(p => p.Model == "new").Select(p => p.RatedGames).ToList();
        Assert.Equal(653, everyone[0]);
        Assert.Equal(700, everyone[^1]);
    }
}
