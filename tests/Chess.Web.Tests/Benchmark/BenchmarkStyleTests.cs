using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkStyleTests
{
    private static Dictionary<string, Reply> Routes(bool styles = true)
    {
        var routes = new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/cost/inputs"] = Serve.Ok(Fixtures.CostInputs),
            ["GET /api/benchmarks"] = Serve.Ok("[]"),
            ["POST /api/benchmarks"] = Serve.Created($$"""{"benchmark":{{Fixtures.RunningBenchmark}},"estimatedPremiumRequests":[]}"""),
        };
        if (styles)
        {
            routes["GET /api/prompt-styles"] = Serve.Ok(PromptStyleFixtures.Json);
        }

        return routes;
    }

    [Fact]
    public void TheCandidateIsSentWithItsEffortAndStyleAndTheOpponentsWithTheirStyle()
    {
        var handler = Serve.Routes(Routes());
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#candidate-style option")));

        cut.Find("#benchmark-name").Input("Style check");
        cut.Find("#effort").Change("high");
        cut.Find("#candidate-style").Change("think");
        cut.Find("#opponent-style").Change("board");
        cut.Find("#start-benchmark").Click();

        cut.WaitForAssertion(() => Assert.Single(handler.Requests, request => request.Method == "POST"));
        var body = Assert.Single(handler.Requests, request => request.Method == "POST").Body!;
        using var json = JsonDocument.Parse(body);
        Assert.Equal("fake-a@high~think", json.RootElement.GetProperty("candidate").GetString());

        // "All" with a styled opponent style sends the list, so the opponents play in that style.
        Assert.Equal(["fake-b~board", "gpt~board", "local~board", "random"],
            json.RootElement.GetProperty("opponents").EnumerateArray().Select(opponent => opponent.GetString()!).ToArray());
    }

    [Fact]
    public void ClassicOnAllSendsNoOpponentListAsBefore()
    {
        var handler = Serve.Routes(Routes());
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#candidate-style option")));

        cut.Find("#benchmark-name").Input("Plain check");
        cut.Find("#start-benchmark").Click();

        var body = Assert.Single(handler.Requests, request => request.Method == "POST").Body!;
        using var json = JsonDocument.Parse(body);
        Assert.Equal("fake-a@medium", json.RootElement.GetProperty("candidate").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("opponents").ValueKind);
    }

    [Fact]
    public void WithoutTheStyleListOnlyClassicIsOfferedAndTheNoticeSaysSo()
    {
        var handler = Serve.Routes(Routes(styles: false));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        var cut = ctx.Render<BenchmarkPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#candidate-style option")));
        Assert.Single(cut.FindAll("#opponent-style option"));
        Assert.Contains("Prompt styles could not be loaded", cut.Find("[data-testid='styles-notice']").TextContent);
    }
}
