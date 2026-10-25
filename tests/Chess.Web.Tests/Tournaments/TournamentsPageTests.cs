using System.Net;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class TournamentsPageTests
{
    private const string Created =
        """{"tournament":{"id":"33333333-3333-3333-3333-333333333333","name":"Autumn","models":["gpt","claude"],"gamesPerPair":2,"status":"running","gamesTotal":2,"gamesFinished":0,"createdAt":"2026-10-24T10:00:00Z","finishedAt":null},"estimatedPremiumRequests":[{"modelId":"gpt","estimatedPremiumRequests":160},{"modelId":"claude","estimatedPremiumRequests":240}]}""";

    [Fact]
    public void ListsTheTournamentsWithTheirProgressAndOnlyRunningOnesCanBeCancelled()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments(Fixtures.RunningTournament, Fixtures.FinishedTournament)),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentsPage>();

        cut.WaitForAssertion(() => Assert.Contains("Summer cup", cut.Markup));
        var rows = cut.FindAll("table.tournament-table tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("1 of 2 games", rows[0].TextContent);
        var progress = rows[0].QuerySelector("[role='progressbar']")!;
        Assert.Equal("1", progress.GetAttribute("aria-valuenow"));
        Assert.Equal("2", progress.GetAttribute("aria-valuemax"));
        Assert.Contains("width: 50%", rows[0].QuerySelector(".progress-bar")!.GetAttribute("style"));
        Assert.NotNull(cut.Find("table.tournament-table").Closest(".table-wrap"));
        Assert.Equal("Running", rows[0].Children[3].TextContent);
        Assert.Single(cut.FindAll("button"), button => button.TextContent.Trim() == "Cancel tournament");
    }

    [Fact]
    public void EstimatesThePremiumRequestsFromTheSelectionBeforeAnythingIsSent()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));

        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        // Two models, two games per pair: each model plays two games, at 80 requests a game and its multiplier.
        cut.WaitForAssertion(() => Assert.Contains("Known total: 400", cut.Markup));
        var rows = cut.FindAll(".tournament-estimate tbody tr");
        Assert.Equal("160", rows[0].Children[2].TextContent);
        Assert.Equal("240", rows[1].Children[2].TextContent);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "POST");
    }

    [Fact]
    public void ChangingGamesPerPairChangesTheEstimate()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        cut.Find("#games-per-pair").Change("4");

        cut.WaitForAssertion(() => Assert.Equal("320", cut.FindAll(".tournament-estimate tbody tr")[0].Children[2].TextContent));
        Assert.Contains("4 games in 1 pair.", cut.Markup);
    }

    [Fact]
    public void AnUnknownMultiplierIsShownAsUnknownAndNamedInTheTotal()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));

        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-random").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("plus 1 model with an unknown multiplier", cut.Markup));
        Assert.Equal("unknown", cut.FindAll(".tournament-estimate tbody tr")[1].Children[2].TextContent);
    }

    [Fact]
    public void StartStaysDisabledUntilTheFormIsValid()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        Assert.True(cut.Find("#start-tournament").HasAttribute("disabled"));

        cut.Find("#tournament-name").Input("Autumn");
        cut.Find("#model-gpt").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Choose at least two models.", cut.Markup));
        Assert.True(cut.Find("#start-tournament").HasAttribute("disabled"));

        cut.Find("#model-claude").Change(true);

        cut.WaitForAssertion(() => Assert.False(cut.Find("#start-tournament").HasAttribute("disabled")));
    }

    [Fact]
    public void ConfirmingPostsTheTournamentAndShowsTheEstimateTheApiReturned()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
            ["POST /api/tournaments"] = Serve.Created(Created),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#tournament-name").Input("Autumn");
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "POST");

        cut.Find("#start-tournament").Click();

        cut.WaitForAssertion(() => Assert.Contains("Started", cut.Markup));
        var post = Assert.Single(handler.Requests, request => request.Method == "POST");
        Assert.Equal("/api/tournaments", post.Target);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal("Autumn", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(["gpt", "claude"], body.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToArray());
        Assert.Equal(2, body.RootElement.GetProperty("gamesPerPair").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("maxConcurrency").GetInt32());

        var banner = cut.Find(".tournament-created").TextContent;
        Assert.Contains("Autumn", banner);
        Assert.Contains("2 games", banner);
        Assert.Contains("gpt: 160", banner);
        Assert.Contains("claude: 240", banner);
    }

    [Fact]
    public void AnApiRefusalIsSaidInPlainWords()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
            ["POST /api/tournaments"] = Serve.Fail(HttpStatusCode.BadRequest),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#tournament-name").Input("Autumn");
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        cut.Find("#start-tournament").Click();

        cut.WaitForAssertion(() => Assert.Contains("The tournament was not created", cut.Markup));
        Assert.Empty(cut.FindAll(".tournament-created"));
    }

    [Fact]
    public void CancelNeedsASecondClickAndOnlyThenDeletes()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments(Fixtures.RunningTournament)),
            [$"DELETE /api/tournaments/{Fixtures.RunningId}"] = Serve.Ok("{}"),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("Cancel tournament", cut.Markup));
        Assert.Contains("btn-danger", Button(cut, "Cancel tournament").ClassName);

        Button(cut, "Cancel tournament").Click();
        Assert.Contains("Abort its queued and running games?", cut.Markup);
        Assert.Contains("btn-danger", Button(cut, "Yes, cancel").ClassName);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "DELETE");

        Button(cut, "Keep running").Click();
        Assert.DoesNotContain("Abort its queued and running games?", cut.Markup);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "DELETE");

        Button(cut, "Cancel tournament").Click();
        Button(cut, "Yes, cancel").Click();

        cut.WaitForAssertion(() => Assert.Single(handler.Requests, request => request.Method == "DELETE"));
        Assert.Equal($"/api/tournaments/{Fixtures.RunningId}", handler.Requests.Single(r => r.Method == "DELETE").Target);
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<TournamentsPage> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

    [Fact]
    public void ModelPickersAreChipsInsideTheFormAndTheStartButtonIsPrimary()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentsPage>();

        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        var pickers = cut.FindAll(".chips input[type='checkbox']");
        Assert.NotEmpty(pickers);
        Assert.All(pickers, picker => Assert.NotNull(picker.Closest(".chip")));
        Assert.NotNull(cut.Find("#tournament-name").Closest(".card"));
        Assert.Contains("btn-primary", cut.Find("#start-tournament").ClassName);
    }

    [Fact]
    public void TheEstimateIsAnInfoCalloutAndItsTableScrollsInsideATableWrap()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));

        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Known total: 400", cut.Markup));
        var estimate = cut.Find(".tournament-estimate");
        Assert.Contains("callout-info", estimate.ClassName);
        Assert.NotNull(estimate.QuerySelector("table")!.Closest(".table-wrap"));
    }
}
