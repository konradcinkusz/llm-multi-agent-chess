using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Ratings;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Ratings;

/// <summary>
/// RatingMemo holds a pure function of the games and moves tables. It must hand back the same view while nothing changed
/// and a new one as soon as a rated game finishes or a move is annotated.
/// </summary>
public class RatingMemoTests
{
    private static (IDbContextFactory<ArenaDbContext> Factory, DbContextOptions<ArenaDbContext> Options) Seeded()
    {
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var db = new ArenaDbContext(options))
        {
            RatingSeed.Populate(db);
            db.SaveChanges();
        }

        return (new RatingSeed.OptionsFactory(options), options);
    }

    [Fact]
    public async Task An_unchanged_store_gives_the_same_view_without_recomputing()
    {
        var (factory, _) = Seeded();
        var memo = new RatingMemo();

        var first = await memo.GetAsync(factory, null, CancellationToken.None);
        var second = await memo.GetAsync(factory, null, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(RatingSeed.RatedOutcomes, first.Rated.Outcomes);
    }

    [Fact]
    public async Task The_view_is_what_a_direct_calculation_gives()
    {
        var (factory, _) = Seeded();

        var view = await new RatingMemo().GetAsync(factory, null, CancellationToken.None);

        var report = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());
        Assert.Equal(report.Models, view.Report.Models);
        Assert.Equal(report.WhiteAdvantage, view.Report.WhiteAdvantage);
        foreach (var cell in view.Matrix)
        {
            Assert.Equal(RatingCalculator.Pairwise(report, RatingSeed.RatedOutcomes, cell.A, cell.B), cell);
        }
    }

    [Fact]
    public async Task Finishing_a_rated_game_changes_the_answer()
    {
        var (factory, options) = Seeded();
        var memo = new RatingMemo();
        var before = await memo.GetAsync(factory, null, CancellationToken.None);

        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-b", "fake-a", "1-0"));
            db.SaveChanges();
        }

        var after = await memo.GetAsync(factory, null, CancellationToken.None);

        Assert.NotSame(before, after);
        Assert.Equal(before.Report.Games + 1, after.Report.Games);
        Assert.Equal(before.Rated.Outcomes.Count + 1, after.Rated.Outcomes.Count);
        Assert.Equal(
            RatingCalculator.Compute(after.Rated.Outcomes, after.Rated.MoveStats, new RatingOptions()).Models,
            after.Report.Models);
    }

    [Fact]
    public async Task An_unfinished_or_unrated_game_does_not_replace_the_view()
    {
        var (factory, options) = Seeded();
        var memo = new RatingMemo();
        var before = await memo.GetAsync(factory, null, CancellationToken.None);

        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", "*"));
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-a", "fake-b", "1-0", rated: false));
            db.SaveChanges();
        }

        Assert.Same(before, await memo.GetAsync(factory, null, CancellationToken.None));
    }

    [Fact]
    public async Task Annotating_a_move_changes_the_answer()
    {
        var (factory, options) = Seeded();
        var memo = new RatingMemo();
        var before = await memo.GetAsync(factory, null, CancellationToken.None);

        using (var db = new ArenaDbContext(options))
        {
            // The post-game analysis fills in the centipawn loss of the two moves fake-b had none for (G2 ply 1, G4 ply 2).
            db.Moves.Single(m => m.GameId == RatingSeed.G2 && m.Ply == 1).CentipawnLoss = 100;
            db.Moves.Single(m => m.GameId == RatingSeed.G4 && m.Ply == 2).CentipawnLoss = 100;
            db.SaveChanges();
        }

        var after = await memo.GetAsync(factory, null, CancellationToken.None);

        Assert.NotSame(before, after);
        Assert.Null(before.Rated.MoveStats.Single(s => s.Model == "fake-b").AverageCentipawnLoss);
        Assert.Equal(72.5, after.Rated.MoveStats.Single(s => s.Model == "fake-b").AverageCentipawnLoss);   // (40 + 100 + 100 + 50) / 4
    }

    [Fact]
    public async Task A_change_of_an_annotation_value_alone_changes_the_answer()
    {
        var (factory, options) = Seeded();
        var memo = new RatingMemo();
        var before = await memo.GetAsync(factory, null, CancellationToken.None);

        using (var db = new ArenaDbContext(options))
        {
            db.Moves.Single(m => m.GameId == RatingSeed.G1 && m.Ply == 1).CentipawnLoss = 25;   // was 20: (25 + 10 + 0 + 30) / 4
            db.SaveChanges();
        }

        var after = await memo.GetAsync(factory, null, CancellationToken.None);

        Assert.NotSame(before, after);
        Assert.Equal(16.25, after.Rated.MoveStats.Single(s => s.Model == "fake-a").AverageCentipawnLoss);
    }

    [Fact]
    public async Task Each_tournament_has_its_own_view_and_a_new_game_only_replaces_the_scopes_it_belongs_to()
    {
        var (factory, options) = Seeded();
        var memo = new RatingMemo();
        var all = await memo.GetAsync(factory, null, CancellationToken.None);
        var spring = await memo.GetAsync(factory, RatingSeed.Spring, CancellationToken.None);
        Assert.Equal(RatingSeed.SpringOutcomes, spring.Rated.Outcomes);

        using (var db = new ArenaDbContext(options))
        {
            db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-b", "fake-a", "0-1"));   // no tournament
            db.SaveChanges();
        }

        Assert.NotSame(all, await memo.GetAsync(factory, null, CancellationToken.None));
        Assert.Same(spring, await memo.GetAsync(factory, RatingSeed.Spring, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_store_gives_an_empty_view()
    {
        var factory = new RatingSeed.OptionsFactory(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var view = await new RatingMemo().GetAsync(factory, null, CancellationToken.None);

        Assert.Empty(view.Report.Models);
        Assert.Empty(view.Matrix);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_computation()
    {
        var (factory, _) = Seeded();
        var memo = new RatingMemo();

        var views = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => memo.GetAsync(factory, null, CancellationToken.None))));

        Assert.All(views, view => Assert.Same(views[0], view));
    }

    [Fact]
    public async Task A_cancelled_request_throws_and_leaves_the_memo_usable()
    {
        var (factory, _) = Seeded();
        var memo = new RatingMemo();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => memo.GetAsync(factory, null, cancelled.Token));

        var view = await memo.GetAsync(factory, null, CancellationToken.None);
        Assert.Equal(5, view.Report.Games);
    }

    [Fact]
    public async Task The_memo_does_not_grow_with_the_tournament_ids_it_is_asked_about()
    {
        var (factory, _) = Seeded();
        var memo = new RatingMemo();

        for (var i = 0; i < RatingMemo.MaxScopes * 3; i++)
        {
            await memo.GetAsync(factory, Guid.NewGuid(), CancellationToken.None);
        }

        Assert.True(memo.ScopeCount <= RatingMemo.MaxScopes, $"{memo.ScopeCount} scopes");
    }
}
