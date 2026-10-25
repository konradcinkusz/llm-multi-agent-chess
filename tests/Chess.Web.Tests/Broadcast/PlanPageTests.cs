using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class PlanPageTests
{
    private const string Start = "2026-10-24T10:00";

    [Fact]
    public void GenerateBuildsThePreviewAndTheEstimateWithoutSavingAnything()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        SelectModels(cut, "gpt", "claude");
        SetWindow(cut, hours: "2");
        cut.Find("[data-testid='plan-generate']").Click();

        // Two games fit a two-hour window at the default pace; both pit gpt against claude.
        Assert.Equal(2, cut.FindAll("[data-testid='plan-preview'] .plan-game").Count);
        var estimate = cut.Find("[data-testid='plan-estimate']").TextContent;
        Assert.Contains("Premium requests for the day (estimate, before saving)", estimate);
        // gpt: 2 games × 40 × 1; claude: 2 games × 40 × 1.5. The estimate is on screen before anything is sent.
        Assert.Contains("Known total: 200", estimate);
        Assert.DoesNotContain(fake.Requests, request => request.Method == "POST");
        Assert.True(IsDisabled(cut, "plan-start-now"));
    }

    [Fact]
    public void GenerateStaysDisabledUntilThereAreTwoModelsToPlay()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);

        SelectModels(cut, "gpt");

        Assert.True(IsDisabled(cut, "plan-generate"));
        Assert.Contains("Choose at least two models.", cut.Markup);
    }

    [Fact]
    public void SavingStoresTheDraftAndOnlyThenEnablesStart()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        GeneratePreview(cut, hours: "2", "gpt", "claude");
        Assert.True(IsDisabled(cut, "plan-start-now"));

        cut.Find("[data-testid='plan-save']").Click();

        cut.WaitForAssertion(() => Assert.False(IsDisabled(cut, "plan-start-now")));
        Assert.Single(fake.Requests, request => request.Method == "POST" && request.Target == "/api/broadcast/plan");
        Assert.DoesNotContain(fake.Requests, request => request.Method == "PUT");
        Assert.True(IsDisabled(cut, "plan-save"));
        Assert.Contains("Saved as", cut.Markup);
    }

    [Fact]
    public void AGameEditedAfterGeneratingIsSavedWithTheDraftAndStartWaitsForIt()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        GeneratePreview(cut, hours: "2", "gpt", "claude");
        cut.Find("[data-testid='plan-save']").Click();
        cut.WaitForAssertion(() => Assert.False(IsDisabled(cut, "plan-start-now")));

        // Moving game 1's players to game 2 changes the colours, so the draft on screen differs from the saved one.
        cut.FindAll("[data-testid='slot-down']")[0].Click();

        Assert.True(IsDisabled(cut, "plan-start-now"));
        Assert.False(IsDisabled(cut, "plan-save"));

        cut.Find("[data-testid='plan-save']").Click();

        cut.WaitForAssertion(() => Assert.False(IsDisabled(cut, "plan-start-now")));
        Assert.Single(fake.Requests, request => request.Method == "PUT");
        Assert.Single(fake.Requests, request => request.Method == "POST" && request.Target == "/api/broadcast/plan");
    }

    [Fact]
    public void StartSendsTheSavedDraftAndLocksItsGames()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        GeneratePreview(cut, hours: "2", "gpt", "claude");
        cut.Find("[data-testid='plan-save']").Click();
        cut.WaitForAssertion(() => Assert.False(IsDisabled(cut, "plan-start-now")));

        cut.Find("[data-testid='plan-start-now']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Running", cut.Find("[data-testid='schedule-row']").TextContent));
        Assert.Contains(fake.Requests, request => request.Method == "POST" && request.Target.EndsWith("/start", StringComparison.Ordinal));
        Assert.True(IsDisabled(cut, "plan-start-now"));
        Assert.True(IsDisabled(cut, "slot-delete"));
    }

    [Fact]
    public void DeletingAGameLowersTheEstimateBeforeSaving()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        GeneratePreview(cut, hours: "2", "gpt", "claude");
        Assert.Contains("Known total: 200", cut.Find("[data-testid='plan-estimate']").TextContent);

        cut.FindAll("[data-testid='slot-delete']")[1].Click();

        Assert.Contains("Known total: 100", cut.Find("[data-testid='plan-estimate']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='plan-preview'] .plan-game"));
        Assert.DoesNotContain(fake.Requests, request => request.Method == "POST");
    }

    [Fact]
    public void AGameWhereAModelMeetsItselfBlocksSaving()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        GeneratePreview(cut, hours: "2", "gpt", "claude");

        cut.Find("[data-testid='slot-black']").Change("gpt");

        Assert.Contains("Game 1: a model cannot play itself.", cut.Markup);
        Assert.True(IsDisabled(cut, "plan-save"));
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

    private static IRenderedComponent<PlanPage> Render(BunitContext ctx)
    {
        var cut = ctx.Render<PlanPage>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading models", cut.Markup));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading schedules", cut.Markup));
        cut.Find("[data-testid='plan-start']").Change(Start);
        return cut;
    }

    private static void SelectModels(IRenderedComponent<PlanPage> cut, params string[] models)
    {
        foreach (var model in models)
        {
            cut.Find($"[data-testid='plan-model-{model}']").Change(true);
        }
    }

    private static void SetWindow(IRenderedComponent<PlanPage> cut, string hours)
    {
        cut.Find("[data-testid='plan-hours']").Change(hours);
    }

    private static void GeneratePreview(IRenderedComponent<PlanPage> cut, string hours, params string[] models)
    {
        SelectModels(cut, models);
        SetWindow(cut, hours);
        cut.Find("[data-testid='plan-generate']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='plan-slot']")));
    }

    private static bool IsDisabled(IRenderedComponent<PlanPage> cut, string testId)
    {
        var elements = cut.FindAll($"[data-testid='{testId}']");
        Assert.NotEmpty(elements);
        return elements.All(element => element.HasAttribute("disabled"));
    }

    [Fact]
    public void ModelPickersAreChipsAndTheTablesScrollInsideTableWraps()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading models", cut.Markup));
        var pickers = cut.FindAll("[data-testid='plan-models'] input[type='checkbox']");
        Assert.NotEmpty(pickers);
        Assert.All(pickers, picker => Assert.NotNull(picker.Closest(".chip")));
        Assert.Contains("btn-primary", cut.Find("[data-testid='plan-generate']").ClassName);

        SelectModels(cut, "gpt", "claude");
        SetWindow(cut, hours: "2");
        cut.Find("[data-testid='plan-generate']").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='plan-preview'] .plan-game").Count));
        Assert.Contains("callout-info", cut.Find("[data-testid='plan-estimate']").ClassName);
        Assert.NotEmpty(cut.FindAll("table"));
        Assert.All(cut.FindAll("table"), table => Assert.NotNull(table.Closest(".table-wrap")));
    }

    [Fact]
    public void StopIsADangerButtonAndTheScheduleTableScrollsInsideATableWrap()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("running");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        Assert.Contains("btn-danger", cut.Find("[data-testid='schedule-stop']").ClassName);
        Assert.NotNull(cut.Find(".plan-schedules").Closest(".table-wrap"));
    }
}
