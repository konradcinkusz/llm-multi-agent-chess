using Bunit;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

/// <summary>The tournament form's cost wording: prices on the model list, and AI credits and USD once the API has estimated them.</summary>
public class TournamentCostTests
{
    // gpt has an estimate from history (12 past moves); claude has no prices, so its credits and USD are unknown.
    private const string CreatedWithCosts =
        """{"tournament":{"id":"33333333-3333-3333-3333-333333333333","name":"Autumn","models":["gpt","claude"],"gamesPerPair":2,"status":"running","gamesTotal":2,"gamesFinished":0,"createdAt":"2026-10-24T10:00:00Z","finishedAt":null},"estimatedPremiumRequests":[{"modelId":"gpt","estimatedPremiumRequests":160,"estimatedAiCredits":24,"estimatedUsd":0.24,"tokenBasis":"history","sampleMoves":12},{"modelId":"claude","estimatedPremiumRequests":240,"estimatedAiCredits":null,"estimatedUsd":null,"tokenBasis":"unknown","sampleMoves":null}]}""";

    [Fact]
    public void EachModelCheckboxShowsItsPriceOrItsMultiplierNeverMultiplierUnknown()
    {
        using var ctx = new BunitContext();
        Wire.Api(ctx, Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        }));

        var cut = ctx.Render<TournamentsPage>();

        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        Assert.Contains("$3.00 / $15.00 per 1M tokens", cut.Find("#model-gpt").Closest("label")!.TextContent);
        Assert.Contains("×1.5 premium requests", cut.Find("#model-claude").Closest("label")!.TextContent);
        Assert.Contains("free", cut.Find("#model-random").Closest("label")!.TextContent);
        Assert.DoesNotContain("multiplier unknown", cut.Markup);
    }

    [Fact]
    public void TheCreatedTournamentShowsTheAiCreditsUsdAndTheirBasisPerModel()
    {
        using var ctx = new BunitContext();
        Wire.Api(ctx, Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
            ["POST /api/tournaments"] = Serve.Created(CreatedWithCosts),
        }));
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#tournament-name").Input("Autumn");
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        cut.Find("#start-tournament").Click();

        cut.WaitForAssertion(() => Assert.Contains("Started", cut.Markup));
        var banner = cut.Find(".tournament-created").TextContent;
        Assert.Contains("gpt: 160", banner);
        Assert.Contains("24 credits · $0.24 (from 12 past moves)", banner);
        Assert.Contains("claude: 240", banner);
        Assert.Contains("price unknown", banner);
    }
}
