using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class PlanPageTests
{
    [Fact]
    public void ThePageAsksForNoStartTimeOrDuration()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);

        Assert.Empty(cut.FindAll("[data-testid='plan-start']"));
        Assert.Empty(cut.FindAll("[data-testid='plan-hours']"));
        Assert.DoesNotContain("Start (UTC)", cut.Markup);
        Assert.True(IsDisabled(cut, "plan-start-now"));
    }

    [Fact]
    public void AddGameNeedsDistinctWhiteAndBlackPlayers()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);

        Assert.True(IsDisabled(cut, "add-game"));

        cut.Find("[data-testid='add-white']").Change("gpt");
        cut.Find("[data-testid='add-black']").Change("gpt");
        Assert.True(IsDisabled(cut, "add-game"));

        cut.Find("[data-testid='add-black']").Change("claude");
        Assert.False(IsDisabled(cut, "add-game"));
    }

    [Fact]
    public void GamesAreAddedOneByOneInOrderWithoutSendingAnything()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        AddGame(cut, white: "gpt", black: "claude");
        AddGame(cut, white: "claude", black: "gpt");

        var rows = cut.FindAll("[data-testid='plan-slot']");
        Assert.Equal(2, rows.Count);
        Assert.Contains("GPT", rows[0].QuerySelector("[data-testid='slot-white']")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claude", rows[1].QuerySelector("[data-testid='slot-white']")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fake.Requests, request => request.Method == "POST");
        Assert.False(IsDisabled(cut, "plan-start-now"));
    }

    [Fact]
    public void TheTotalCostCoversEveryGameAndFallsWhenOneIsDeleted()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        Assert.Empty(cut.FindAll("[data-testid='plan-total']"));

        AddGame(cut, white: "gpt", black: "claude");
        AddGame(cut, white: "claude", black: "gpt");

        // gpt: 2 games × 40 × 1; claude: 2 games × 40 × 1.5.
        var total = cut.Find("[data-testid='plan-total']").TextContent;
        Assert.Contains("Estimated total for 2 games", total);
        Assert.Contains("200", total);

        cut.FindAll("[data-testid='slot-delete']")[1].Click();

        Assert.Contains("Estimated total for 1 game:", cut.Find("[data-testid='plan-total']").TextContent);
        Assert.Contains("100", cut.Find("[data-testid='plan-total']").TextContent);
    }

    [Fact]
    public void UpAndDownSwapNeighbouringGames()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "claude");
        AddGame(cut, white: "claude", black: "gpt");
        var first = cut.FindAll("[data-testid='slot-white']")[0].TextContent;

        cut.FindAll("[data-testid='slot-down']")[0].Click();

        Assert.Equal(first, cut.FindAll("[data-testid='slot-white']")[1].TextContent);
    }

    [Fact]
    public void StartSavesTheGamesAndStartsThemInOneClick()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "claude");
        AddGame(cut, white: "claude", black: "gpt");

        cut.Find("[data-testid='plan-start-now']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Running", cut.Find("[data-testid='schedule-row']").TextContent));
        Assert.Equal(
            ["POST /api/broadcast/plan", "PUT", "POST start"],
            fake.Requests.Where(request => request.Method is "POST" or "PUT")
                .Select(request => request.Target.EndsWith("/start", StringComparison.Ordinal) ? "POST start"
                    : request.Method == "PUT" ? "PUT" : $"{request.Method} {request.Target}"));
        Assert.Empty(cut.FindAll("[data-testid='plan-slot']"));
    }

    [Fact]
    public void StartIsDisabledWhileAnotherBroadcastPlays()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("running", "On air");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));

        AddGame(cut, white: "gpt", black: "claude");

        Assert.True(IsDisabled(cut, "plan-start-now"));
    }

    [Fact]
    public void ThePauseResumeAndStopControlsCallTheRouteOfTheirCommand()
    {
        var fake = new FakeBroadcast();
        var id = fake.SeedSchedule("running");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        cut.Find("[data-testid='schedule-pause']").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-resume']")));
        cut.Find("[data-testid='schedule-resume']").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-pause']")));
        cut.Find("[data-testid='schedule-stop']").Click();

        cut.WaitForAssertion(() => Assert.Equal("cancelled", fake.StatusOf(id)));
        Assert.Equal(
            [
                $"POST /api/broadcast/schedules/{id:D}/pause",
                $"POST /api/broadcast/schedules/{id:D}/resume",
                $"POST /api/broadcast/schedules/{id:D}/stop",
            ],
            fake.Requests.Where(request => request.Method == "POST" && request.Target.Contains("/schedules/"))
                .Select(request => $"{request.Method} {request.Target}"));
    }

    [Fact]
    public void ARefusedCommandShowsTheAPIsReason()
    {
        var fake = new FakeBroadcast();
        var id = fake.SeedSchedule("running");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-stop']")));

        // The list is stale: the schedule finished elsewhere, so the API refuses the stop with 409.
        fake.SetStatus(id, "finished");
        cut.Find("[data-testid='schedule-stop']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not stop: The schedule is finished.", cut.Markup));
    }

    [Fact]
    public void StartIsOfferedForADraftInTheListAndNotWhileAnotherScheduleIsPlaying()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("running", "On air");
        var draft = fake.SeedSchedule("draft", "Tomorrow");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);

        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='schedule-row']").Count));
        Assert.True(IsDisabled(cut, "schedule-start"));
        Assert.DoesNotContain(fake.Requests, request => request.Target.EndsWith($"{draft:D}/start", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTablesScrollInsideTableWrapsAndStopIsADangerButton()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("running");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "claude");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        Assert.Contains("btn-danger", cut.Find("[data-testid='schedule-stop']").ClassName);
        Assert.Contains("btn-primary", cut.Find("[data-testid='plan-start-now']").ClassName);
        Assert.NotEmpty(cut.FindAll("table"));
        Assert.All(cut.FindAll("table"), table => Assert.NotNull(table.Closest(".table-wrap")));
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

    private static bool IsDisabled(IRenderedComponent<PlanPage> cut, string testId)
    {
        var elements = cut.FindAll($"[data-testid='{testId}']");
        Assert.NotEmpty(elements);
        return elements.All(element => element.HasAttribute("disabled"));
    }
}
