using Chess.Api.Persistence;
using Chess.Api.Ratings;
using Chess.Api.Results;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Ratings;

/// <summary>
/// The InMemory provider evaluates queries that PostgreSQL cannot translate (lesson #78), so a green test on it does not
/// prove a query runs. These translate each rating and results query with the Npgsql provider. No server is contacted:
/// <c>ToQueryString</c> only compiles the query. A query Npgsql cannot translate throws here.
/// </summary>
public class PostgresTranslationTests
{
    private static ArenaDbContext Npgsql() => new(
        new DbContextOptionsBuilder<ArenaDbContext>().UseNpgsql("Host=localhost;Database=chess").Options);

    [Fact]
    public void The_rated_game_rows_order_before_they_project()
    {
        using var db = Npgsql();

        var sql = RatedGameLoader.GameRows(db, Guid.NewGuid()).ToQueryString();

        Assert.Contains("ORDER BY", sql);
        Assert.Contains("tournament_id", sql);
    }

    [Fact]
    public void The_move_statistics_are_summed_in_the_database()
    {
        using var db = Npgsql();

        var sql = RatedGameLoader.MoveGroups(db, null).ToQueryString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("sum(", sql);
        Assert.DoesNotContain("fen_after", sql);
    }

    [Fact]
    public void The_data_version_is_two_small_aggregates()
    {
        using var db = Npgsql();

        var games = RatedGameLoader.GamesVersion(db, Guid.NewGuid()).ToQueryString();
        var moves = RatedGameLoader.MovesVersion(db, null).ToQueryString();

        Assert.Contains("count(*)", games);
        Assert.Contains("max(", games);
        Assert.Contains("count(*)", moves);
        Assert.Contains("sum(", moves);
    }

    [Fact]
    public void The_breakdown_move_statistics_are_summed_in_the_database_without_fen_after()
    {
        using var db = Npgsql();

        var sql = ResultsGames.MoveTotalsOf(db, ResultsGames.Decided(db)).ToQueryString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("sum(", sql);
        Assert.DoesNotContain("fen_after", sql);
    }

    [Fact]
    public void The_history_queries_translate()
    {
        using var db = Npgsql();
        var snapshots = db.RatingSnapshots.AsNoTracking();

        Assert.Contains("GROUP BY", RatingHistory.Spans(snapshots).ToQueryString());
        var thinned = RatingHistory.Thinned(snapshots, 4, [1, 7, 9]).ToQueryString();
        Assert.Contains("rated_games % @stride", thinned);
        Assert.Contains("ORDER BY", RatingHistory.Points(RatingHistory.Thinned(snapshots, 4, [1])).ToQueryString());
    }
}
