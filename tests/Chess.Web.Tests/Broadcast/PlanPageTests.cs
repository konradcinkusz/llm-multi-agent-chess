using System.Text.Json;
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
        // One call validates, saves and starts: no draft is saved first, and no second call can fail halfway.
        Assert.Equal(
            ["POST /api/broadcast/schedules"],
            fake.Requests.Where(request => request.Method is "POST" or "PUT" or "DELETE")
                .Select(request => $"{request.Method} {request.Target}"));
        Assert.Empty(cut.FindAll("[data-testid='plan-slot']"));

        var sent = SentSchedule(fake);
        Assert.True(sent.Start);
        Assert.Equal([("gpt", "claude"), ("claude", "gpt")], sent.Games.Select(game => (game.White.ModelId, game.Black.ModelId)));
    }

    [Fact]
    public void TheRandomPlayerIsSentWithTheRandomKindAndStartsWithoutAnError()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "random");
        AddGame(cut, white: "random", black: "claude");

        cut.Find("[data-testid='plan-start-now']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Running", cut.Find("[data-testid='schedule-row']").TextContent));
        Assert.DoesNotContain("Could not start", cut.Markup);
        var games = SentSchedule(fake).Games;
        Assert.Equal([new PlayerSpec("llm", "gpt"), new PlayerSpec("random", "random")], [games[0].White, games[0].Black]);
        Assert.Equal([new PlayerSpec("random", "random"), new PlayerSpec("llm", "claude")], [games[1].White, games[1].Black]);
    }

    [Fact]
    public void AStartTheApiRefusesLeavesNoDraftInTheListAndNothingElseWasSent()
    {
        var fake = new FakeBroadcast();
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "claude");

        // The page does not know yet: another broadcast went on air after the list loaded.
        fake.SeedSchedule("running", "On air");
        cut.Find("[data-testid='plan-start-now']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not start: Stop or finish the broadcast", cut.Markup));
        Assert.Equal(["On air"], fake.Schedules.Select(schedule => schedule.Name));
        Assert.DoesNotContain(fake.Requests, request => request.Method is "PUT" or "DELETE" || request.Target.EndsWith("/plan", StringComparison.Ordinal));
        // The games stay on the page, so the user can start again once the other broadcast is over.
        Assert.Single(cut.FindAll("[data-testid='plan-slot']"));
        Assert.Single(cut.FindAll("[data-testid='schedule-row']"));
    }

    [Fact]
    public void TheEstimateTotalDoesNotRepeatTheCreditsUnit()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);

        // claude has no prices and random is free, so the known credits are 0.
        AddGame(cut, white: "claude", black: "random");

        var total = cut.Find("[data-testid='plan-total']").TextContent;
        Assert.Contains("0 credits", total);
        Assert.DoesNotContain("credits AI credits", total);
    }

    [Fact]
    public void TheRandomPlayerIsFreeInThePriceAndTheLastColumnAlike()
    {
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, new FakeBroadcast());
        var cut = Render(ctx);
        AddGame(cut, white: "gpt", black: "random");

        var rows = cut.FindAll("[data-testid='plan-estimate'] tbody tr");
        var random = rows.Single(row => row.Children[0].TextContent == "Random");
        Assert.Equal("free", random.Children[1].TextContent);
        Assert.Equal("free", random.Children[6].TextContent);
        Assert.DoesNotContain("price unknown", random.TextContent);
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
        cut.Find("[data-testid='schedule-stop-confirm']").Click();

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
        cut.Find("[data-testid='schedule-stop-confirm']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not stop: The schedule is finished.", cut.Markup));
    }

    [Fact]
    public void StopTakesASecondClickAndKeepPlayingBacksOut()
    {
        var fake = new FakeBroadcast();
        var id = fake.SeedSchedule("running");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-stop']")));

        cut.Find("[data-testid='schedule-stop']").Click();

        // The first click only asks; nothing was sent and the schedule still plays.
        Assert.Contains("Stop this broadcast?", cut.Markup);
        Assert.DoesNotContain(fake.Requests, request => request.Target.EndsWith("/stop", StringComparison.Ordinal));
        Assert.Equal("running", fake.StatusOf(id));

        cut.Find("[data-testid='schedule-stop-keep']").Click();

        Assert.DoesNotContain("Stop this broadcast?", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='schedule-stop']"));
        Assert.DoesNotContain(fake.Requests, request => request.Target.EndsWith("/stop", StringComparison.Ordinal));

        cut.Find("[data-testid='schedule-stop']").Click();
        cut.Find("[data-testid='schedule-stop-confirm']").Click();

        cut.WaitForAssertion(() => Assert.Equal("cancelled", fake.StatusOf(id)));
    }

    [Fact]
    public void ADraftCanBeDeletedFromTheListAndOnlyADraft()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("running", "On air");
        var draft = fake.SeedSchedule("draft", "Left over");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = Render(ctx);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='schedule-row']").Count));

        // One Delete button: the running row has none.
        cut.Find("[data-testid='schedule-delete']").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        Assert.Contains($"DELETE /api/broadcast/schedules/{draft:D}",
            fake.Requests.Select(request => $"{request.Method} {request.Target}"));
        Assert.Equal(["On air"], fake.Schedules.Select(schedule => schedule.Name));
        Assert.Empty(cut.FindAll("[data-testid='schedule-delete']"));
    }

    [Fact]
    public void TheListRefreshesWhileADayPlaysSoStartIsFreedWhenItEnds()
    {
        var fake = new FakeBroadcast();
        var id = fake.SeedSchedule("running", "On air");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = ctx.Render<PlanPage>(parameters => parameters.Add(page => page.RefreshInterval, TimeSpan.FromMilliseconds(50)));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        AddGame(cut, white: "gpt", black: "claude");
        Assert.True(IsDisabled(cut, "plan-start-now"));
        Assert.Contains("Another broadcast is playing", cut.Markup);

        // The day ends elsewhere; nothing is pressed on this page.
        fake.SetStatus(id, "finished");

        cut.WaitForAssertion(() => Assert.False(IsDisabled(cut, "plan-start-now")));
        Assert.DoesNotContain("Another broadcast is playing", cut.Markup);
        Assert.Contains("Finished", cut.Find("[data-testid='schedule-row']").TextContent);
    }

    [Fact]
    public void TheListDoesNotPollWhenNothingIsPlayingAndStopsWhenThePageIsDisposed()
    {
        var fake = new FakeBroadcast();
        fake.SeedSchedule("draft", "Tomorrow");
        using var ctx = new BunitContext();
        PlanWire.Api(ctx, fake);
        var cut = ctx.Render<PlanPage>(parameters => parameters.Add(page => page.RefreshInterval, TimeSpan.FromMilliseconds(30)));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='schedule-row']")));
        var listed = ListCount(fake);

        Thread.Sleep(300);
        Assert.Equal(listed, ListCount(fake));

        var running = new FakeBroadcast();
        running.SeedSchedule("running");
        using var other = new BunitContext();
        PlanWire.Api(other, running);
        var polled = other.Render<PlanPage>(parameters => parameters.Add(page => page.RefreshInterval, TimeSpan.FromMilliseconds(30)));
        polled.WaitForAssertion(() => Assert.True(ListCount(running) > 2));

        other.Dispose();
        Thread.Sleep(100);
        var afterDispose = ListCount(running);
        Thread.Sleep(300);
        Assert.Equal(afterDispose, ListCount(running));
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

    private static int ListCount(FakeBroadcast fake) =>
        fake.Requests.Count(request => request.Method == "GET" && request.Target == "/api/broadcast/schedules");

    /// <summary>The body of the one create-schedule call the page sent.</summary>
    private static CreateScheduleRequest SentSchedule(FakeBroadcast fake)
    {
        var request = Assert.Single(fake.Requests, request => request.Method == "POST" && request.Target == "/api/broadcast/schedules");
        return JsonSerializer.Deserialize<CreateScheduleRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
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
