using Bunit;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

/// <summary>The planner's cost wording: prices on the model list, and AI credits and USD once the draft is saved (03 §10.2).</summary>
public class PlanCostTests
{
    private const string Start = "2026-10-24T10:00";

    [Fact]
    public void EachModelCheckboxShowsItsPrice()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);

        var cut = Render(ctx);

        Assert.Contains("$3.00 / $15.00 per 1M tokens", cut.Find("[data-testid='plan-model-gpt']").Closest("label")!.TextContent);
        Assert.Contains("×1.5 premium requests", cut.Find("[data-testid='plan-model-claude']").Closest("label")!.TextContent);
        Assert.DoesNotContain("multiplier unknown", cut.Markup);
    }

    [Fact]
    public void SavingTheDraftShowsTheAiCreditsAndUsdWithTheirBasis()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.Find("[data-testid='plan-model-gpt']").Change(true);
        cut.Find("[data-testid='plan-model-claude']").Change(true);
        cut.Find("[data-testid='plan-hours']").Change("2");
        cut.Find("[data-testid='plan-generate']").Click();
        Assert.DoesNotContain("plan-cost", cut.Markup);

        cut.Find("[data-testid='plan-save']").Click();

        cut.WaitForAssertion(() => Assert.Contains("AI credits and USD", cut.Find("[data-testid='plan-cost']").TextContent));
        var cost = cut.Find("[data-testid='plan-cost']").TextContent;
        // gpt has no stored moves, so its tokens are assumed; claude has no prices, so its cost is unknown.
        Assert.Contains("assumed tokens per move", cost);
        Assert.Contains("price unknown", cost);
        Assert.Contains("Known total:", cost);
    }

    private static IRenderedComponent<PlanPage> Render(BunitContext ctx)
    {
        var cut = ctx.Render<PlanPage>();
        cut.WaitForAssertion(() => Assert.Contains("Start (UTC)", cut.Markup));
        cut.Find("[data-testid='plan-start']").Change(Start);
        return cut;
    }
}
