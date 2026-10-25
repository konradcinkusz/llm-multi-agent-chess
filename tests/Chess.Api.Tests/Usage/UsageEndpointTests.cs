using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Usage;
using Xunit;

namespace Chess.Api.Tests.Usage;

public class UsageEndpointTests
{
    private const double Rate = 0.01;
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");
    private static readonly PlayerSpec Random = new("random", "random");

    [Fact]
    public async Task AFinishedFakeGameReportsUsageThatIsTheSumOfItsAttempts()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();
        var gameId = await StartAndWaitAsync(client, FakeA, FakeB);

        var usage = await client.GetFromJsonAsync<GameUsageDto>($"/api/games/{gameId}/usage");
        var attempts = await AttemptsAsync(client, gameId);

        Assert.NotNull(usage);
        Assert.Equal(gameId, usage.GameId);
        Assert.Equal(Rate, usage.UsdPerAiCredit);
        Assert.Equal(attempts.Count(a => a.Ply % 2 == 1), usage.White.Requests);
        Assert.Equal(attempts.Count(a => a.Ply % 2 == 0), usage.Black.Requests);
        Assert.True(usage.TotalAiCredits > 0);
        Assert.Equal(attempts.Sum(a => (long)a.InputTokens!.Value), usage.White.InputTokens + usage.Black.InputTokens);
        Assert.Equal(attempts.Sum(a => (long)a.OutputTokens!.Value), usage.White.OutputTokens + usage.Black.OutputTokens);

        // Prices are 1.0 and 4.0 credits per million tokens, and nothing was reported, so every side is estimated.
        Assert.Equal(GameCostCalculator.Estimated, usage.White.CostBasis);
        Assert.Equal(GameCostCalculator.Estimated, usage.Black.CostBasis);
        Assert.Equal(ExpectedCredits(attempts, ply => ply % 2 == 1), usage.White.AiCredits!.Value, 12);
        Assert.Equal(ExpectedCredits(attempts, ply => ply % 2 == 0), usage.Black.AiCredits!.Value, 12);
        Assert.Equal(usage.White.AiCredits!.Value + usage.Black.AiCredits!.Value, usage.TotalAiCredits!.Value, 12);
        Assert.Equal(usage.TotalAiCredits!.Value * Rate, usage.TotalEstimatedUsd!.Value, 12);
    }

    [Fact]
    public async Task TheDollarRateComesFromTheOwnersConfiguration()
    {
        using var app = new UsageApp(usdPerAiCredit: 0.02);
        using var client = app.CreateClient();
        var gameId = await StartAndWaitAsync(client, FakeA, FakeB);

        var usage = await client.GetFromJsonAsync<GameUsageDto>($"/api/games/{gameId}/usage");
        var models = await client.GetFromJsonAsync<ModelDto[]>("/api/models");

        Assert.NotNull(usage);
        Assert.Equal(0.02, usage.UsdPerAiCredit);
        Assert.Equal(usage.TotalAiCredits!.Value * 0.02, usage.TotalEstimatedUsd!.Value, 12);
        // Prices in dollars per million tokens scale with the rate: 1.0 credit is 0.02 USD at this rate.
        Assert.Equal(0.02, models!.Single(m => m.Id == "fake-a").Pricing!.InputUsdPerMillionTokens!.Value, 12);
    }

    [Fact]
    public async Task TheRandomSideIsFreeAndTheLlmSideIsPriced()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();
        var gameId = await StartAndWaitAsync(client, FakeA, Random);

        var usage = await client.GetFromJsonAsync<GameUsageDto>($"/api/games/{gameId}/usage");

        Assert.NotNull(usage);
        Assert.Equal(GameCostCalculator.Free, usage.Black.CostBasis);
        Assert.Equal(0, usage.Black.Requests);
        Assert.Equal(0.0, usage.Black.AiCredits);
        Assert.Equal(GameCostCalculator.Estimated, usage.White.CostBasis);
        Assert.True(usage.White.AiCredits > 0);
        Assert.Equal(usage.White.AiCredits!.Value, usage.TotalAiCredits!.Value, 12);
    }

    [Fact]
    public async Task AnUnknownGameHasNoUsage()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync($"/api/games/{Guid.NewGuid()}/usage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AttemptsCarryTheirUsageAndThePricesAreInDollarsPerMillionTokens()
    {
        using var app = new UsageApp();
        using var client = app.CreateClient();
        var gameId = await StartAndWaitAsync(client, FakeA, FakeB);

        var attempts = await AttemptsAsync(client, gameId);
        var models = await client.GetFromJsonAsync<ModelDto[]>("/api/models");

        Assert.All(attempts, a => Assert.True(a.InputTokens > 0 && a.OutputTokens > 0));
        Assert.All(attempts, a => Assert.Null(a.RejectionKind));
        Assert.NotNull(models);
        var fakeA = models.Single(m => m.Id == "fake-a");
        Assert.Equal(0.01, fakeA.Pricing!.InputUsdPerMillionTokens!.Value, 12);
        Assert.Equal(0.04, fakeA.Pricing.OutputUsdPerMillionTokens!.Value, 12);
        Assert.Equal(0.001, fakeA.Pricing.CachedInputUsdPerMillionTokens!.Value, 12);
    }

    [Fact]
    public async Task TheAutoModelIsHiddenUnlessTheOwnerAsksForIt()
    {
        using var hidden = new UsageApp();
        using var hiddenClient = hidden.CreateClient();
        var listed = await hiddenClient.GetFromJsonAsync<ModelDto[]>("/api/models");

        Assert.DoesNotContain(listed!, m => m.Id == "auto");
        Assert.Null(listed!.Single(m => m.Id == "random").Pricing);

        using var shown = new UsageApp(showAutoModel: true);
        using var shownClient = shown.CreateClient();
        var all = await shownClient.GetFromJsonAsync<ModelDto[]>("/api/models");

        var auto = Assert.Single(all!, m => m.Id == "auto");
        Assert.Null(auto.Pricing);
    }

    /// <summary>Starts the game and waits until it is finished, then returns its id.</summary>
    internal static async Task<Guid> StartAndWaitAsync(HttpClient client, PlayerSpec white, PlayerSpec black)
    {
        var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest(white, black, Rated: false));
        created.EnsureSuccessStatusCode();
        var game = (await created.Content.ReadFromJsonAsync<GameSummaryDto>())!;

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{game.Id}", deadline.Token);
            if (detail!.Summary.State == "finished")
            {
                return game.Id;
            }

            await Task.Delay(20, deadline.Token);
        }
    }

    internal static async Task<List<AttemptDto>> AttemptsAsync(HttpClient client, Guid gameId)
    {
        var page = await client.GetFromJsonAsync<PageDto<AttemptDto>>($"/api/attempts?gameId={gameId}&pageSize=200");
        return page!.Items.ToList();
    }

    /// <summary>Credits from the stored tokens: (input × 1.0 + output × 4.0) per million, for the attempts on one side.</summary>
    internal static double ExpectedCredits(IEnumerable<AttemptDto> attempts, Func<int, bool> sideOfPly) =>
        attempts.Where(a => sideOfPly(a.Ply)).Sum(a => (a.InputTokens!.Value * 1.0 + a.OutputTokens!.Value * 4.0) / 1_000_000);
}
