using Chess.Api.Contracts;
using Chess.Api.Results;
using Xunit;
using static Chess.Api.Tests.Results.ResultsFixtures;
using Fixture = Chess.Api.Tests.Results.ResultsFixtures.BreakdownFixture;

namespace Chess.Api.Tests.Results;

public class ResultsBreakdownTests
{
    [Fact]
    public async Task Tournament_breakdown_counts_wins_draws_losses_and_forfeits_per_model()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = NewContext(name))
        {
            Fixture.Seed(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var rows = await ResultsBreakdown.ReadAsync(db, Fixture.Tournament, CancellationToken.None);

        Assert.Equal(["A", "B"], rows.Select(r => r.Model));
        var a = rows[0];
        // A: won game 1, drew game 2, lost game 3 (White forfeit) and game 4 (Black forfeit).
        Assert.Equal(4, a.Games);
        Assert.Equal(1, a.Wins);
        Assert.Equal(1, a.Draws);
        Assert.Equal(2, a.Losses);
        Assert.Equal(2, a.ForfeitLosses);
        // Two moves: attempts 1 and 3, so half were legal first time.
        Assert.Equal(2, a.Moves);
        Assert.Equal(0.5, a.FirstAttemptLegalRate);
        Assert.Equal(150, a.AvgLatencyMs);
        Assert.Equal(10, a.AvgInputTokens);
        Assert.Equal(5, a.AvgOutputTokens);
        Assert.Equal(20, a.AverageCentipawnLoss);

        var b = rows[1];
        // B: lost game 1, drew game 2, won game 3 and game 4 (A forfeited there, not B).
        Assert.Equal(4, b.Games);
        Assert.Equal(2, b.Wins);
        Assert.Equal(1, b.Draws);
        Assert.Equal(1, b.Losses);
        Assert.Equal(0, b.ForfeitLosses);
        Assert.Equal(2, b.Moves);
        Assert.Equal(0.5, b.FirstAttemptLegalRate);
        Assert.Equal(175, b.AvgLatencyMs);
        Assert.Null(b.AvgInputTokens);
        Assert.Null(b.AvgOutputTokens);
        Assert.Null(b.AverageCentipawnLoss);
    }

    [Fact]
    public async Task Without_a_tournament_every_decided_game_counts_and_unfinished_games_never_do()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = NewContext(name))
        {
            Fixture.Seed(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var rows = await ResultsBreakdown.ReadAsync(db, null, CancellationToken.None);

        // The fixture's game outside the tournament is a decided A win; the unfinished game is excluded.
        var a = rows.Single(r => r.Model == "A");
        Assert.Equal(5, a.Games);
        Assert.Equal(2, a.Wins);
        Assert.Equal(2, a.ForfeitLosses);
    }

    [Fact]
    public async Task A_tournament_with_no_finished_games_has_no_rows()
    {
        var name = Guid.NewGuid().ToString();
        var tournament = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            seed.Games.Add(ResultsFixtures.Game(Guid.NewGuid(), "A", "B", "*", null, null, tournamentId: tournament));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        Assert.Empty(await ResultsBreakdown.ReadAsync(db, tournament, CancellationToken.None));
    }
}
