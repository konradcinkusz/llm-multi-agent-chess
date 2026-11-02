using Bunit;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

/// <summary>The planner's cost wording: the estimate of the games on screen, in AI credits and USD with their basis (03 §10.2).</summary>
public class PlanCostTests
{
    [Fact]
    public void TheEstimateShowsEachModelsAiCreditsUsdAndTheirBasis()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        AddGame(cut, "gpt", "claude");
        AddGame(cut, "claude", "gpt");

        // Two games, so each model plays two: 2 × 40 moves a side = 80 moves, at the assumed tokens for gpt.
        // 80 × (700 × $3 + 300 × $15) / 1M = $0.528 = 52.8 credits. claude has no prices.
        var rows = cut.FindAll("[data-testid='plan-estimate'] tbody tr");
        Assert.Equal("$3.00 / $15.00 per 1M tokens", rows[0].Children[1].TextContent);
        Assert.Equal("52.8 credits", rows[0].Children[4].TextContent);
        Assert.Equal("$0.53", rows[0].Children[5].TextContent);
        Assert.Equal("assumed tokens per move", rows[0].Children[6].TextContent);
        Assert.Equal("price unknown", rows[1].Children[6].TextContent);
        Assert.DoesNotContain(fake.Requests, request => request.Method == "POST");
    }

    private static void AddGame(IRenderedComponent<PlanPage> cut, string white, string black)
    {
        cut.Find("[data-testid='add-white']").Change(white);
        cut.Find("[data-testid='add-black']").Change(black);
        cut.Find("[data-testid='add-game']").Click();
    }

    private static IRenderedComponent<PlanPage> Render(BunitContext ctx)
    {
        var cut = ctx.Render<PlanPage>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading models", cut.Markup));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading schedules", cut.Markup));
        return cut;
    }
}
