using Chess.Api.Contracts;
using Chess.Api.Persistence.Entities;
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
        var rows = await ResultsBreakdown.ReadAsync(db, Fixture.Tournament, [], 0.01, CancellationToken.None);

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
        Assert.Null(a.AvgEstimatedUsdPerGame);
        // Two of A's four games were forfeits it lost.
        Assert.Equal(0.5, a.ForfeitRate);

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
        Assert.Equal(0, b.ForfeitRate);
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
        var rows = await ResultsBreakdown.ReadAsync(db, null, [], 0.01, CancellationToken.None);

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
        Assert.Empty(await ResultsBreakdown.ReadAsync(db, tournament, [], 0.01, CancellationToken.None));
    }

    [Fact]
    public async Task The_estimated_cost_per_game_averages_the_llm_sides_that_have_a_known_cost()
    {
        var name = Guid.NewGuid().ToString();
        var game1 = Guid.NewGuid();
        var game2 = Guid.NewGuid();
        var game3 = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            // A plays White in game 1 and Black in game 2; game 3 is B against the random player, which is free.
            seed.Games.AddRange(
                Game(game1, "A", "B", "1-0", "checkmate", T0.AddMinutes(1)),
                Game(game2, "B", "A", "0-1", "checkmate", T0.AddMinutes(2)),
                Game(game3, "B", "random", "1-0", "checkmate", T0.AddMinutes(3)));
            seed.MoveAttempts.AddRange(
                // $2 per million input and $10 per million output: 1 000 in and 500 out is $0.007.
                Reply(game1, 1, input: 1000, output: 500),
                // 2 000 in and 1 000 out is $0.014.
                Reply(game2, 2, input: 2000, output: 1000),
                // B has no price, so its reply has no cost.
                Reply(game2, 1, input: 9000, output: 9000),
                Reply(game3, 2, input: 100, output: 100));
            await seed.SaveChangesAsync();
        }

        var models = new List<ModelDto>
        {
            new("A", "A", null, true, new ModelPricingDto(2, 10, null)),
            new("B", "B", null, true, null),
        };
        await using var db = NewContext(name);
        var rows = await ResultsBreakdown.ReadAsync(db, null, models, 0.01, CancellationToken.None);

        var a = rows.Single(r => r.Model == "A");
        Assert.Equal((0.007 + 0.014) / 2, a.AvgEstimatedUsdPerGame!.Value, 10);
        var b = rows.Single(r => r.Model == "B");
        Assert.Null(b.AvgEstimatedUsdPerGame);
        var random = rows.Single(r => r.Model == "random");
        Assert.Null(random.AvgEstimatedUsdPerGame);
    }

    [Fact]
    public async Task A_side_whose_replies_are_partly_unpriced_is_averaged_over_the_priced_replies()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await using (var seed = NewContext(name))
        {
            seed.Games.Add(Game(game, "A", "B", "1-0", "checkmate", T0.AddMinutes(1)));
            seed.MoveAttempts.AddRange(
                Reply(game, 1, input: 1000, output: 500),
                // No token counts were recorded for this reply, so it has no estimate.
                Reply(game, 3, input: null, output: null));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext(name);
        var rows = await ResultsBreakdown.ReadAsync(db, null,
            [new ModelDto("A", "A", null, true, new ModelPricingDto(2, 10, null))], 0.01, CancellationToken.None);

        Assert.Equal(0.007, rows.Single(r => r.Model == "A").AvgEstimatedUsdPerGame!.Value, 10);
    }

    private static MoveAttemptEntity Reply(Guid game, int ply, int? input, int? output) => new()
    {
        GameId = game,
        Ply = ply,
        Number = 1,
        Prompt = "",
        Legal = true,
        LatencyMs = 1,
        CreatedAt = T0,
        InputTokens = input,
        OutputTokens = output,
    };
}
