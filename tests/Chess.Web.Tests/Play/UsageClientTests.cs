using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Play;

public class UsageClientTests
{
    private const string GameId = "55555555-5555-5555-5555-555555555555";

    [Fact]
    public async Task TheUsageOfAGameIsReadFromItsUsageEndpoint()
    {
        var json = $$"""
            {"gameId":"{{GameId}}",
             "white":{"color":"White","modelId":"gpt","requests":2,"inputTokens":300,"outputTokens":40,"reasoningTokens":5,"cachedInputTokens":0,"aiCredits":1.25,"estimatedUsd":0.0125,"costBasis":"estimated"},
             "black":{"color":"Black","modelId":"claude","requests":0,"inputTokens":0,"outputTokens":0,"reasoningTokens":0,"cachedInputTokens":0,"aiCredits":null,"estimatedUsd":null,"costBasis":"unknown"},
             "totalAiCredits":1.25,"totalEstimatedUsd":0.0125,"usdPerAiCredit":0.01}
            """;
        var handler = new FakeHandler(_ => Canned.Json(json));
        var api = new ArenaApiClient(Canned.Client(handler));

        var usage = await api.GetGameUsageAsync(Guid.Parse(GameId));

        Assert.Equal($"/api/games/{GameId}/usage", Assert.Single(handler.Requests).Target);
        Assert.Equal(0.01, usage.UsdPerAiCredit);
        Assert.Equal("estimated", usage.White.CostBasis);
        Assert.Null(usage.Black.EstimatedUsd);
        Assert.Equal(1.25, usage.TotalAiCredits);
    }
}
