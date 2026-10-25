using System.Net;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkPageTests
{
    private const string Created =
        $$"""{"benchmark":{{Fixtures.RunningBenchmark}},"estimatedPremiumRequests":[]}""";

    /// <summary>The routes every render needs: the models, the cost inputs and the runs list.</summary>
    private static Dictionary<string, Reply> Defaults(string benchmarks = "[]") => new()
    {
        ["GET /api/models"] = Serve.Ok(Fixtures.Models),
        ["GET /api/cost/inputs"] = Serve.Ok(Fixtures.CostInputs),
        ["GET /api/benchmarks"] = Serve.Ok(benchmarks),
    };

    private static FakeHandler Mount(BunitContext ctx, Dictionary<string, Reply> routes)
    {
        var handler = Serve.Routes(routes);
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        return handler;
    }

    private static JsonElement SentBody(FakeHandler handler) =>
        JsonDocument.Parse(handler.Requests.Single(request => request.Method == "POST").Body!).RootElement;

    [Fact]
    public void EffortsAreOfferedOnlyForAModelThatListsThem()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults());
        var cut = ctx.Render<BenchmarkPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#effort")));
        var efforts = cut.Find("#effort").QuerySelectorAll("option").Select(option => option.TextContent).ToArray();
        Assert.Equal(new[] { "low", "medium (default)", "high" }, efforts);

        cut.Find("#candidate").Change("gpt");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#effort")));
    }

    [Fact]
    public void ACustomModelIsMarkedAsCustomInTheCandidateList()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults());
        var cut = ctx.Render<BenchmarkPage>();

        cut.WaitForAssertion(() => Assert.Contains("My model (custom)", cut.Find("#candidate").TextContent));
        Assert.DoesNotContain("Random", cut.Find("#candidate").TextContent);
    }

    [Fact]
    public void ByDefaultTheOpponentsAreEveryOtherEnabledModel()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults());

        var cut = ctx.Render<BenchmarkPage>();

        // fake-b, gpt, the custom model and the random player: not the candidate, not Auto, not the disabled model.
        cut.WaitForAssertion(() => Assert.Contains("All other enabled models (4)", cut.Markup));
        Assert.Empty(cut.FindAll("#opponent-fake-a"));
    }

    [Fact]
    public void ChoosingOpponentsListsThemAndStartNeedsOne()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults());
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#start-benchmark")));

        cut.Find("#benchmark-name").Input("Spring check");
        cut.Find("#opponents-choose").Change(true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#opponent-random")));
        Assert.True(cut.Find("#start-benchmark").HasAttribute("disabled"));
        Assert.Contains("Choose at least one opponent.", cut.Markup);

        cut.Find("#opponent-random").Change(true);

        cut.WaitForAssertion(() => Assert.False(cut.Find("#start-benchmark").HasAttribute("disabled")));
    }

    [Fact]
    public void StartPostsTheCandidateWithItsEffortAndTheChosenOpponents()
    {
        var routes = Defaults();
        routes["POST /api/benchmarks"] = Serve.Created(Created);
        using var ctx = new BunitContext();
        var handler = Mount(ctx, routes);
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#effort")));

        cut.Find("#benchmark-name").Input("Spring check");
        cut.Find("#effort").Change("high");
        cut.Find("#opponents-choose").Change(true);
        cut.Find("#opponent-fake-b").Change(true);
        cut.Find("#opponent-random").Change(true);
        cut.Find("#games-per-opponent").Change("2");
        cut.Find("#start-benchmark").Click();

        cut.WaitForAssertion(() => Assert.Contains("Started Spring check with 4 games.", cut.Find(".benchmark-created").TextContent));
        var body = SentBody(handler);
        Assert.Equal("Spring check", body.GetProperty("name").GetString());
        Assert.Equal("fake-a@high", body.GetProperty("candidate").GetString());
        Assert.Equal(new[] { "fake-b", "random" }, body.GetProperty("opponents").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal(2, body.GetProperty("gamesPerOpponent").GetInt32());
        Assert.Equal(2, body.GetProperty("maxConcurrency").GetInt32());
        Assert.Contains("/benchmark/44444444-4444-4444-4444-444444444444", cut.Markup);
    }

    [Fact]
    public void AllOtherModelsIsSentAsNoOpponentListSoTheApiChoosesThem()
    {
        var routes = Defaults();
        routes["POST /api/benchmarks"] = Serve.Created(Created);
        using var ctx = new BunitContext();
        var handler = Mount(ctx, routes);
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#start-benchmark")));

        cut.Find("#benchmark-name").Input("Spring check");
        cut.Find("#start-benchmark").Click();

        cut.WaitForAssertion(() => Assert.Contains("Started", cut.Markup));
        Assert.Equal(JsonValueKind.Null, SentBody(handler).GetProperty("opponents").ValueKind);
        Assert.Equal("fake-a@medium", SentBody(handler).GetProperty("candidate").GetString());
    }

    [Fact]
    public void TheEstimateCountsTheGamesAndWarnsWhenTheRunIsLarge()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults());
        var cut = ctx.Render<BenchmarkPage>();

        cut.Find("#candidate").Change("gpt");

        // Four opponents at the default 30 games: 120 games, and the candidate's 4 800 moves cost more than the USD threshold.
        cut.WaitForAssertion(() => Assert.Contains("120 games", cut.Find("[data-testid='benchmark-estimate']").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid='benchmark-large']"));

        cut.Find("#games-per-opponent").Change("2");

        cut.WaitForAssertion(() => Assert.Contains("8 games", cut.Find("[data-testid='benchmark-estimate']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='benchmark-large']"));
    }

    [Fact]
    public void TheRunsListShowsProgressAndOnlyARunningOneCanBeCancelled()
    {
        using var ctx = new BunitContext();
        Mount(ctx, Defaults(Fixtures.Benchmarks(Fixtures.RunningBenchmark, Fixtures.FinishedBenchmark)));

        var cut = ctx.Render<BenchmarkPage>();

        cut.WaitForAssertion(() => Assert.Contains("Summer check", cut.Markup));
        var rows = cut.FindAll("table.benchmark-table tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("1 of 4 games", rows[0].TextContent);
        Assert.Contains("Running", rows[0].TextContent);
        Assert.Contains("Finished", rows[1].TextContent);
        Assert.Single(cut.FindAll("button"), button => button.TextContent.Trim() == "Cancel benchmark");
    }

    [Fact]
    public void AnApiRefusalIsShownAndNothingIsStarted()
    {
        var routes = Defaults();
        routes["POST /api/benchmarks"] = Serve.Fail(HttpStatusCode.BadRequest);
        using var ctx = new BunitContext();
        Mount(ctx, routes);
        var cut = ctx.Render<BenchmarkPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#start-benchmark")));

        cut.Find("#benchmark-name").Input("Spring check");
        cut.Find("#start-benchmark").Click();

        cut.WaitForAssertion(() => Assert.Contains("The benchmark was not created:", cut.Markup));
        Assert.Empty(cut.FindAll(".benchmark-created"));
    }
}
