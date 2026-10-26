using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Admin;
using Chess.Web.Tests.Broadcast;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Admin;

public class RequestsPageTests
{
    private const string QueuedId = "aaaaaaaa-0000-4000-8000-000000000001";

    [Fact]
    public void ShowsTheQueueWithACancelButtonForEachQueuedRequest()
    {
        var handler = Api(queue: LiveFixtures.Queue(
            queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1),
            recent: LiveFixtures.Request("Bo", "claude", "gemini", "played")));
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=requests-queued] tbody tr")));
        Assert.Contains("Ana", cut.Find("[data-testid=requests-queued]").TextContent);
        Assert.Single(cut.FindAll("[data-testid=requests-queued] button"));
        Assert.Contains("played", cut.Find("[data-testid=requests-recent]").TextContent);
    }

    [Fact]
    public void CancelPostsTheCancelToTheRequest()
    {
        var handler = Api(queue: LiveFixtures.Queue(queued: LiveFixtures.Request("Ana", "gpt", "claude", "queued", 1)));
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=requests-queued] button")));

        cut.Find("[data-testid=requests-queued] button").Click();

        cut.WaitForAssertion(() => Assert.Contains(handler.Requests, r => r.Method == "POST" && r.Target.EndsWith("/cancel", StringComparison.Ordinal)));
        Assert.Contains("credit is kept", cut.Find("[data-testid=requests-message]").TextContent);
    }

    [Fact]
    public void SaysSoWhenTheQueueIsNotAnswered()
    {
        var handler = Api(queue: null);
        using var ctx = new BunitContext();

        var cut = Render(ctx, handler);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=requests-unavailable]")));
        Assert.Empty(cut.FindAll("[data-testid=requests-queued]"));
    }

    [Fact]
    public async Task SimulateSendsTheMessageThroughTheIntake()
    {
        var handler = Api(queue: LiveFixtures.Queue());
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=requests-simulate]")));

        cut.Find("#sim-payer-id").Change("viewer-1");
        cut.Find("#sim-payer-name").Change("Ana");
        cut.Find("#sim-text").Change("!g c1 vs d3 think");
        await cut.Find("[data-testid=requests-simulate] form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains(handler.Requests, r => r.Target == "/api/requests/simulate"));
        var body = handler.Requests.Single(r => r.Target == "/api/requests/simulate").Body!;
        Assert.Contains("\"text\":\"!g c1 vs d3 think\"", body);
        Assert.Contains("\"payerDisplay\":\"Ana\"", body);
        Assert.Contains("Sent to the intake", cut.Find("[data-testid=requests-message]").TextContent);
    }

    [Fact]
    public async Task TheSimulateFormGoesAwayWhenTheApiIsNotInMockMode()
    {
        var handler = Api(queue: LiveFixtures.Queue(), simulate: HttpStatusCode.Forbidden);
        using var ctx = new BunitContext();
        var cut = Render(ctx, handler);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=requests-simulate]")));

        cut.Find("#sim-text").Change("!g c1 vs d3");
        await cut.Find("[data-testid=requests-simulate] form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=requests-simulate]")));
        Assert.Contains("not in Mock mode", cut.Find("[data-testid=requests-message]").TextContent);
    }

    private static IRenderedComponent<RequestsPage> Render(BunitContext ctx, FakeHandler handler)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        return ctx.Render<RequestsPage>();
    }

    /// <summary>Answers the queue (null: 404), the cancel and the simulate calls. Everything else is a 404.</summary>
    private static FakeHandler Api(string? queue, HttpStatusCode simulate = HttpStatusCode.OK) => new(r =>
    {
        if (r.Method == "GET" && r.Target == "/api/requests/queue")
        {
            return queue is null ? Canned.Json("{}", HttpStatusCode.NotFound) : Canned.Json(queue);
        }

        if (r.Method == "POST" && r.Target == "/api/requests/simulate")
        {
            return simulate == HttpStatusCode.OK ? Canned.Json("{}") : Canned.Json("{}", simulate);
        }

        if (r.Method == "POST" && r.Target.EndsWith("/cancel", StringComparison.Ordinal))
        {
            return Canned.Json("{}");
        }

        return Canned.Json("{}", HttpStatusCode.NotFound);
    });
}
