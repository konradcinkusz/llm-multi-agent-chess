using Bunit;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class PlanStyleTests
{
    [Fact]
    public void TheTickedStylesAddStyledPlayersToBothPickers()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        Assert.DoesNotContain("gpt~think", OptionValues(cut, "add-white"));

        cut.Find("[data-testid='plan-style-think']").Change(true);

        Assert.Contains("gpt~think", OptionValues(cut, "add-white"));
        Assert.Contains("claude~think", OptionValues(cut, "add-black"));
        Assert.Contains("gpt", OptionValues(cut, "add-white"));
    }

    [Fact]
    public void AStyledPlayerCanBeAddedToAGame()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        cut.Find("[data-testid='plan-style-think']").Change(true);

        cut.Find("[data-testid='add-white']").Change("gpt~think");
        cut.Find("[data-testid='add-black']").Change("claude");
        cut.Find("[data-testid='add-game']").Click();

        Assert.Contains("Think", cut.Find("[data-testid='slot-white']").TextContent);
    }

    [Fact]
    public void WithoutTheStyleListOnlyClassicIsOfferedAndTheNoticeSaysSo()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast(stylesAvailable: false));
        var cut = Render(ctx);

        Assert.Empty(cut.FindAll("[data-testid='plan-styles']"));
        Assert.Contains("Prompt styles could not be loaded", cut.Find("[data-testid='styles-notice']").TextContent);
        Assert.DoesNotContain("gpt~think", OptionValues(cut, "add-white"));
    }

    private static string[] OptionValues(IRenderedComponent<PlanPage> cut, string testId) =>
        [.. cut.FindAll($"[data-testid='{testId}'] option").Select(option => option.GetAttribute("value")!)];

    private static IRenderedComponent<PlanPage> Render(BunitContext ctx)
    {
        var cut = ctx.Render<PlanPage>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading models", cut.Markup));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading schedules", cut.Markup));
        return cut;
    }
}
