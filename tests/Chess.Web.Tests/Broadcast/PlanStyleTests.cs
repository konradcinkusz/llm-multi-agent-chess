using Bunit;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class PlanStyleTests
{
    private const string Start = "2026-10-24T10:00";

    [Fact]
    public void TheScheduleIsPlannedWithTheStyledPlayers()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        cut.Find("[data-testid='plan-model-gpt']").Change(true);
        cut.Find("[data-testid='plan-model-claude']").Change(true);
        cut.Find("[data-testid='plan-style-think']").Change(true);
        cut.Find("[data-testid='plan-hours']").Change("2");
        cut.Find("[data-testid='plan-generate']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='plan-slot']")));
        var players = cut.FindAll("[data-testid='slot-white'] option").Select(option => option.GetAttribute("value")!).Distinct().Order().ToArray();
        Assert.Equal(["claude", "claude~think", "gpt", "gpt~think"], players);
    }

    [Fact]
    public void TheStyleChipsCountThePlayersTheyWouldAdd()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.Find("[data-testid='plan-model-gpt']").Change(true);
        cut.Find("[data-testid='plan-model-claude']").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Think, then move", cut.Find("[data-testid='plan-styles']").TextContent));
        Assert.Contains("2 players", cut.Find("[data-testid='plan-styles']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='styles-notice']"));
    }

    [Fact]
    public void WithoutTheStyleListOnlyClassicIsOfferedAndTheNoticeSaysSo()
    {
        var fake = new FakeBroadcast(stylesAvailable: false);
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='plan-styles'] .chip")));
        Assert.Contains("Prompt styles could not be loaded", cut.Find("[data-testid='styles-notice']").TextContent);
    }

    private static IRenderedComponent<PlanPage> Render(BunitContext ctx)
    {
        var cut = ctx.Render<PlanPage>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading models", cut.Markup));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading schedules", cut.Markup));
        cut.Find("[data-testid='plan-start']").Change(Start);
        return cut;
    }
}
