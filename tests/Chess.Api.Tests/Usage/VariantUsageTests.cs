using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Usage;
using Xunit;

namespace Chess.Api.Tests.Usage;

/// <summary>A player id with a reasoning effort (<c>model@effort</c>, 03 §12.1): accepted, and priced by its base model (03 §10.2).</summary>
public class VariantUsageTests
{
    private const double Rate = 0.01;
    private static readonly PlayerSpec FakeAHigh = new("llm", "fake-a@high");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");

    [Fact]
    public async Task A_game_with_a_variant_is_accepted_finishes_and_is_priced_by_its_base_model()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();
        var gameId = await UsageEndpointTests.StartAndWaitAsync(client, FakeAHigh, FakeB);

        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{gameId}");
        var usage = await client.GetFromJsonAsync<GameUsageDto>($"/api/games/{gameId}/usage");
        var attempts = await UsageEndpointTests.AttemptsAsync(client, gameId);

        Assert.Equal("fake-a@high", detail!.Summary.White.ModelId);
        Assert.Equal("finished", detail.Summary.State);
        Assert.NotNull(usage);
        // Not unknown: the base model's prices price every reply, so the side is estimated.
        Assert.Equal(GameCostCalculator.Estimated, usage.White.CostBasis);
        Assert.Equal("fake-a@high", usage.White.ModelId);
        Assert.Equal(UsageEndpointTests.ExpectedCredits(attempts, ply => ply % 2 == 1), usage.White.AiCredits!.Value, 12);
        Assert.Equal(usage.White.AiCredits!.Value * Rate, usage.White.EstimatedUsd!.Value, 12);
        Assert.NotNull(usage.TotalEstimatedUsd);
    }

    [Theory]
    [InlineData("fake-a@ultra")]
    [InlineData("fake-a@")]
    [InlineData("fake-a@high@low")]
    [InlineData("nope@high")]
    [InlineData("@high")]
    public async Task An_effort_the_model_does_not_offer_or_a_malformed_id_is_a_400(string player)
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("llm", player), FakeB, Rated: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_offered_effort_is_accepted_and_a_plain_model_still_is()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("llm", "fake-a@low"), new PlayerSpec("llm", "fake-b"), Rated: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
