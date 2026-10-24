using Chess.Api.Persistence;
using Chess.Api.Ratings;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Ratings;

public class RatedGameLoaderTests
{
    [Fact]
    public async Task Only_finished_rated_games_become_outcomes()
    {
        var factory = RatingSeed.SeededFactory(Guid.NewGuid().ToString());

        var rated = await RatedGameLoader.LoadAsync(factory, null, CancellationToken.None);

        // Unrated, interrupted and queued games are absent (RATING.md §1).
        Assert.Equal(RatingSeed.RatedOutcomes, rated.Outcomes);
    }

    [Fact]
    public async Task Move_statistics_count_each_side_and_ignore_excluded_games()
    {
        var factory = RatingSeed.SeededFactory(Guid.NewGuid().ToString());

        var rated = await RatedGameLoader.LoadAsync(factory, null, CancellationToken.None);

        Assert.Equal(RatingSeed.RatedStats, rated.MoveStats);
    }

    [Fact]
    public async Task Tournament_filter_keeps_only_that_tournaments_games()
    {
        var factory = RatingSeed.SeededFactory(Guid.NewGuid().ToString());

        var rated = await RatedGameLoader.LoadAsync(factory, RatingSeed.Spring, CancellationToken.None);

        Assert.Equal(RatingSeed.SpringOutcomes, rated.Outcomes);
        Assert.Equal(RatingSeed.SpringStats, rated.MoveStats);
    }

    [Theory]
    [InlineData("1-0", 1.0)]
    [InlineData("0-1", 0.0)]
    [InlineData("1/2-1/2", 0.5)]
    public async Task Results_map_to_the_white_score(string result, double whiteScore)
    {
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(name).Options;
        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", result));
            db.SaveChanges();
        }

        var rated = await RatedGameLoader.LoadAsync(new RatingSeed.OptionsFactory(options), null, CancellationToken.None);

        Assert.Equal(new GameOutcome("fake-a", "fake-b", whiteScore), Assert.Single(rated.Outcomes));
    }

    [Fact]
    public async Task A_random_side_is_rated_as_random_whatever_id_it_stores()
    {
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(name).Options;
        var game = RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", "1-0");
        game.WhiteKind = "random";
        game.WhiteModel = "stale-id";
        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(game);
            db.SaveChanges();
        }

        var rated = await RatedGameLoader.LoadAsync(new RatingSeed.OptionsFactory(options), null, CancellationToken.None);

        Assert.Equal(new GameOutcome("random", "fake-b", 1.0), Assert.Single(rated.Outcomes));
    }

    [Fact]
    public async Task An_unexpected_result_on_a_rated_game_is_rejected_not_scored()
    {
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(name).Options;
        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", "?-?"));
            db.SaveChanges();
        }

        var factory = new RatingSeed.OptionsFactory(options);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RatedGameLoader.LoadAsync(factory, null, CancellationToken.None));
        Assert.Contains("?-?", ex.Message);
    }
}
