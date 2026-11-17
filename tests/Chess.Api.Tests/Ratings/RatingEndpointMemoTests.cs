using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Rating;
using Xunit;

namespace Chess.Api.Tests.Ratings;

/// <summary>The endpoints read one memoised view: they agree with each other and follow the data as it changes.</summary>
public sealed class RatingEndpointMemoTests
{
    [Fact]
    public async Task Every_matrix_cell_is_what_the_pairwise_endpoint_and_a_direct_call_give()
    {
        using var app = new SeededRatingsApp();
        var client = app.CreateClient();
        var report = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());

        var matrix = (await client.GetFromJsonAsync<MatrixDto>("/api/ratings/matrix"))!;

        Assert.Equal(3 * 2, matrix.Cells.Count);
        foreach (var cell in matrix.Cells)
        {
            var pairwise = await client.GetFromJsonAsync<PairwiseCoefficient>($"/api/ratings/pairwise?a={cell.A}&b={cell.B}");
            Assert.Equal(pairwise, cell);
            Assert.Equal(RatingCalculator.Pairwise(report, RatingSeed.RatedOutcomes, cell.A, cell.B), cell);
        }

        Assert.Equal(report.Models.Select(m => m.Model), matrix.Models);
    }

    [Fact]
    public async Task A_model_against_itself_is_still_answered_by_the_pairwise_endpoint()
    {
        using var app = new SeededRatingsApp();
        var report = RatingCalculator.Compute(RatingSeed.RatedOutcomes, RatingSeed.RatedStats, new RatingOptions());

        var self = await app.CreateClient().GetFromJsonAsync<PairwiseCoefficient>("/api/ratings/pairwise?a=fake-a&b=fake-a");

        Assert.Equal(RatingCalculator.Pairwise(report, RatingSeed.RatedOutcomes, "fake-a", "fake-a"), self);
    }

    [Fact]
    public async Task A_game_finishing_between_two_requests_changes_all_three_endpoints()
    {
        using var app = new SeededRatingsApp();
        var client = app.CreateClient();
        var board = (await client.GetFromJsonAsync<LeaderboardDto>("/api/ratings"))!;
        var matrix = (await client.GetFromJsonAsync<MatrixDto>("/api/ratings/matrix"))!;
        var pair = (await client.GetFromJsonAsync<PairwiseCoefficient>("/api/ratings/pairwise?a=fake-a&b=fake-b"))!;
        Assert.Equal(5, board.Games);

        app.Seed(db => db.Games.Add(RatingSeed.Game(Guid.NewGuid(), "fake-b", "fake-a", "1-0")));

        var boardAfter = (await client.GetFromJsonAsync<LeaderboardDto>("/api/ratings"))!;
        var matrixAfter = (await client.GetFromJsonAsync<MatrixDto>("/api/ratings/matrix"))!;
        var pairAfter = (await client.GetFromJsonAsync<PairwiseCoefficient>("/api/ratings/pairwise?a=fake-a&b=fake-b"))!;
        Assert.Equal(6, boardAfter.Games);
        Assert.NotEqual(matrix.Cells, matrixAfter.Cells);
        Assert.NotEqual(pair, pairAfter);
        Assert.Equal(matrixAfter.Cells.Single(c => c.A == "fake-a" && c.B == "fake-b"), pairAfter);
        Assert.Equal(pairAfter.HeadToHeadGames, pair.HeadToHeadGames + 1);
    }

    [Fact]
    public async Task The_same_numbers_come_back_on_a_repeated_request()
    {
        using var app = new SeededRatingsApp();
        var client = app.CreateClient();

        var first = await client.GetStringAsync("/api/ratings/matrix");
        var second = await client.GetStringAsync("/api/ratings/matrix");

        Assert.Equal(first, second);
    }
}
