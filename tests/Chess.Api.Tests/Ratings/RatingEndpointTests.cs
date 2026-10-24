using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Rating;
using Xunit;

namespace Chess.Api.Tests.Ratings;

public sealed class RatingEndpointTests(SeededRatingsApp app) : IClassFixture<SeededRatingsApp>
{
    [Fact]
    public async Task Leaderboard_equals_a_direct_calculation_on_the_same_outcomes()
    {
        var expected = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());

        var dto = await GetAsync<LeaderboardDto>(app.CreateClient(), "/api/ratings");

        Assert.Equal(expected.Models, dto.Models);
        Assert.Equal(expected.WhiteAdvantage, dto.WhiteAdvantage);
        Assert.Equal(5, dto.Games);
        Assert.True(dto.MoveQualityAvailable, "fake-a has centipawn losses, so move quality is available");
    }

    [Fact]
    public async Task Leaderboard_for_one_tournament_uses_only_its_games()
    {
        var expected = RatingCalculator.Compute(RatingSeed.SpringOutcomes, RatingSeed.SpringStats, new RatingOptions());

        var dto = await GetAsync<LeaderboardDto>(app.CreateClient(), $"/api/ratings?tournamentId={RatingSeed.Spring}");

        Assert.Equal(expected.Models, dto.Models);
        Assert.Equal(2, dto.Games);
    }

    [Fact]
    public async Task Pairwise_equals_a_direct_pairwise_call()
    {
        var report = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());
        var expected = RatingCalculator.Pairwise(report, RatingSeed.RatedOutcomes, "fake-a", "fake-b");

        var dto = await GetAsync<PairwiseCoefficient>(app.CreateClient(), "/api/ratings/pairwise?a=fake-a&b=fake-b");

        Assert.Equal(expected, dto);
    }

    [Fact]
    public async Task Pairwise_for_a_model_without_rated_games_is_404_problem_details()
    {
        var response = await app.CreateClient().GetAsync("/api/ratings/pairwise?a=fake-a&b=gpt-unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("gpt-unknown", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pairwise_without_both_models_is_400()
    {
        var response = await app.CreateClient().GetAsync("/api/ratings/pairwise?a=fake-a");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Matrix_holds_every_ordered_pair_of_distinct_models_in_leaderboard_order()
    {
        var report = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());

        var dto = await GetAsync<MatrixDto>(app.CreateClient(), "/api/ratings/matrix");

        Assert.Equal(report.Models.Select(m => m.Model), dto.Models);
        Assert.Equal(dto.Models.Count * (dto.Models.Count - 1), dto.Cells.Count);
        foreach (var cell in dto.Cells)
        {
            Assert.Equal(RatingCalculator.Pairwise(report, RatingSeed.RatedOutcomes, cell.A, cell.B), cell);
        }

        // V3: the coefficient is antisymmetric, so each pair's two cells sum to 1.
        foreach (var cell in dto.Cells)
        {
            var reverse = dto.Cells.Single(c => c.A == cell.B && c.B == cell.A);
            Assert.Equal(1.0, cell.Rqc + reverse.Rqc, 9);
        }
    }

    [Fact]
    public async Task An_empty_database_gives_an_empty_leaderboard()
    {
        using var empty = new RatingsApp();
        var client = empty.CreateClient();

        var leaderboard = await GetAsync<LeaderboardDto>(client, "/api/ratings");
        var matrix = await GetAsync<MatrixDto>(client, "/api/ratings/matrix");

        Assert.Empty(leaderboard.Models);
        Assert.Equal(0, leaderboard.Games);
        Assert.False(leaderboard.MoveQualityAvailable);
        Assert.Empty(matrix.Cells);
    }

    [Fact]
    public async Task Ratings_are_recomputed_on_every_request()
    {
        using var fresh = new RatingsApp();
        fresh.Seed(RatingSeed.Populate);
        var client = fresh.CreateClient();
        var before = await GetAsync<LeaderboardDto>(client, "/api/ratings");

        fresh.Seed(db => db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-b", "fake-a", "1-0")));
        var after = await GetAsync<LeaderboardDto>(client, "/api/ratings");

        Assert.Equal(before.Games + 1, after.Games);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
