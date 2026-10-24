using System.Net;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Admin;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Admin;

public class AttemptsPageTests
{
    private static readonly Guid Game = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ShowsEachAttemptInTheOrderTheApiReturnsIt()
    {
        var handler = Api(_ => Page(1, 50, 2, Attempt(1, "PROMPT ONE"), Attempt(2, "PROMPT TWO", legal: false)));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.attempt").Count));
        Assert.Equal("/api/attempts?page=1", handler.Requests.Last(r => r.Target.StartsWith("/api/attempts", StringComparison.Ordinal)).Target);
        var prompts = cut.FindAll("li.attempt .prompt-text").Select(p => p.TextContent).ToArray();
        Assert.Equal(new[] { "PROMPT ONE", "PROMPT TWO" }, prompts);
    }

    [Fact]
    public void PromptAndReplyAreHeldInsideAnExpandableDetailsElement()
    {
        var handler = Api(_ => Page(1, 50, 1, Attempt(1, "PROMPT ONE")));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("details")));
        var details = cut.Find("details");
        Assert.NotNull(details.QuerySelector("summary"));
        Assert.Equal("PROMPT ONE", details.QuerySelector(".prompt-text")!.TextContent);
        Assert.Equal("e4", details.QuerySelector(".reply-text")!.TextContent);
    }

    [Fact]
    public void IllegalAttemptsAreMarkedAndShowTheirError()
    {
        var handler = Api(_ => Page(1, 50, 2, Attempt(1, "PROMPT ONE"), Attempt(2, "PROMPT TWO", legal: false)));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.attempt").Count));
        var illegal = cut.FindAll("li.attempt")[1];
        Assert.Contains("failed-attempt", illegal.ClassName);
        Assert.Equal("ILLEGAL", illegal.QuerySelector(".fail-tag")!.TextContent);
        Assert.Equal("Error: illegal move", illegal.QuerySelector(".error-text")!.TextContent);
        Assert.Empty(cut.FindAll("li.attempt")[0].QuerySelectorAll(".fail-tag"));
    }

    [Fact]
    public async Task GameFilterSendsTheGameIdAndRejectsAnythingThatIsNotAGuid()
    {
        var handler = Api(_ => Page(1, 50, 0));
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Contains("/api/attempts?page=1", handler.Requests.Select(r => r.Target)));
        var requestsBefore = handler.Requests.Count;

        cut.Find("#attempts-game").Change("not-a-guid");
        await cut.Find("form").SubmitAsync();

        Assert.Equal("Game ID must be a GUID, as shown in the games list.", cut.Find(".field-error").TextContent);
        Assert.Equal(requestsBefore, handler.Requests.Count);

        cut.Find("#attempts-game").Change(Game.ToString());
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains($"gameId={Game:D}", handler.Requests[^1].Target));
        Assert.Empty(cut.FindAll(".field-error"));
        Assert.Equal($"/api/attempts?gameId={Game:D}&page=1", handler.Requests[^1].Target);
    }

    [Fact]
    public async Task ModelFilterSendsTheChosenModel()
    {
        var handler = Api(_ => Page(1, 50, 0));
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("#attempts-model option").Count));

        cut.Find("#attempts-model").Change("m2");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Equal("/api/attempts?model=m2&page=1", handler.Requests[^1].Target));
    }

    [Fact]
    public async Task ReplyFilterShowsOnlyLegalOrIllegalAttemptsOfThePage()
    {
        var handler = Api(_ => Page(1, 50, 2, Attempt(1, "PROMPT ONE"), Attempt(2, "PROMPT TWO", legal: false)));
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.attempt").Count));

        cut.Find("#attempts-legal").Change("illegal");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("li.attempt");
            Assert.Single(rows);
            Assert.Equal("PROMPT TWO", rows[0].QuerySelector(".prompt-text")!.TextContent);
        });
        Assert.Contains("applies to the attempts on this page only", cut.Find(".note").TextContent);

        cut.Find("#attempts-legal").Change("legal");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Equal("PROMPT ONE", cut.Find("li.attempt .prompt-text").TextContent));
    }

    [Fact]
    public void PagingRequestsTheNextPageAndDisablesNextOnTheLastPage()
    {
        var handler = Api(target => target.Contains("page=2", StringComparison.Ordinal)
            ? Page(2, 1, 2, Attempt(2, "PROMPT TWO"))
            : Page(1, 1, 2, Attempt(1, "PROMPT ONE")));
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Equal("PROMPT ONE", cut.Find("li.attempt .prompt-text").TextContent));

        var prev = cut.FindAll(".pagination button")[0];
        var next = cut.FindAll(".pagination button")[1];
        Assert.True(prev.HasAttribute("disabled"));
        Assert.False(next.HasAttribute("disabled"));

        next.Click();

        cut.WaitForAssertion(() => Assert.Equal("PROMPT TWO", cut.Find("li.attempt .prompt-text").TextContent));
        Assert.Equal("/api/attempts?page=2", handler.Requests[^1].Target);
        Assert.Equal("Page 2 of 2", cut.Find(".pagination span").TextContent);
        Assert.True(cut.FindAll(".pagination button")[1].HasAttribute("disabled"));
    }

    [Fact]
    public void GameLinkPointsAtTheGamePage()
    {
        var handler = Api(_ => Page(1, 50, 1, Attempt(1, "PROMPT ONE")));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.attempt a")));
        Assert.Equal($"/games/{Game:D}", cut.Find("li.attempt a").GetAttribute("href"));
    }

    [Fact]
    public void SaysSoWhenTheAttemptsCannotBeLoaded()
    {
        var handler = Api(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Contains("could not be loaded", cut.Find(".error-msg").TextContent));
        Assert.Empty(cut.FindAll("li.attempt"));
    }

    [Fact]
    public void AFailedModelListDoesNotHideTheLog()
    {
        var handler = new FakeHandler(r => r.Target.StartsWith("/api/models", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Page(1, 50, 1, Attempt(1, "PROMPT ONE")));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.attempt")));
        Assert.Single(cut.FindAll("#attempts-model option")); // only "All models"
    }

    private static IRenderedComponent<AttemptsPage> Render(BunitContext ctx, FakeHandler handler)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        return ctx.Render<AttemptsPage>();
    }

    // Models are answered here; every other request goes to `attempts`.
    private static FakeHandler Api(Func<string, HttpResponseMessage> attempts) =>
        new(r => r.Target.StartsWith("/api/models", StringComparison.Ordinal)
            ? Canned.Json(JsonSerializer.Serialize(new[]
            {
                new ModelDto("m1", "Model One", null, true),
                new ModelDto("m2", "Model Two", 1.0, true),
            }, JsonOptions))
            : attempts(r.Target));

    private static AttemptDto Attempt(int ply, string prompt, bool legal = true) => new(
        Game, ply, 1, ply % 2 == 1 ? "m1" : "m2", prompt,
        legal ? "e4" : "Nf9", legal ? "e4" : null, legal, 120, legal ? null : "illegal move",
        DateTimeOffset.Parse("2026-10-24T10:00:00Z"));

    private static HttpResponseMessage Page(int page, int pageSize, int total, params AttemptDto[] items) =>
        Canned.Json(JsonSerializer.Serialize(new PageDto<AttemptDto>(items, page, pageSize, total), JsonOptions));
}
