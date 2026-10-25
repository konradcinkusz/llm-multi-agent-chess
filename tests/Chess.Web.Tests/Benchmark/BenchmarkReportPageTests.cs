using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkReportPageTests
{
    private const string Id = "44444444-4444-4444-4444-444444444444";
    private const string Markdown = "# Spring check\n\nCandidate fake-a@high.\n";

    private static Dictionary<string, Reply> Routes(string report) => new()
    {
        [$"GET /api/benchmarks/{Id}/report"] = Serve.Ok(report),
        [$"GET /api/benchmarks/{Id}/report?format=md"] = new(HttpStatusCode.OK, Markdown),
    };

    private static IRenderedComponent<BenchmarkReportPage> Render(BunitContext ctx, Dictionary<string, Reply> routes)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(Serve.Routes(routes))));
        return ctx.Render<BenchmarkReportPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(Id)));
    }

    [Fact]
    public void TheSummaryTheOpponentsAndTheFingerprintOfAFinishedRunAreShown()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes(Fixtures.Report));

        cut.WaitForAssertion(() => Assert.Contains("Spring check", cut.Find("h1").TextContent));
        var summary = cut.Find("[data-testid='benchmark-summary']").TextContent;
        Assert.Contains("75.0 %", summary);
        Assert.Contains("1500", summary);
        Assert.Contains("95 % interval +1400 to +1600", summary);
        Assert.Contains("provisional", summary);

        var rows = cut.FindAll("table[data-testid='benchmark-opponents'] tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Equal("fake-b", rows[0].QuerySelector("th")!.TextContent);
        Assert.Equal("+∞ (+120 to +∞)", rows[0].Children[7].TextContent);
        Assert.Equal("+0 (−∞ to +∞)", rows[1].Children[7].TextContent);
        Assert.Equal("50.0 %", rows[1].Children[6].TextContent);

        var fingerprint = cut.Find("[data-testid='benchmark-fingerprint']").TextContent;
        Assert.Contains("1.2.3", fingerprint);
        Assert.Contains("RATING-1", fingerprint);
        Assert.Contains("abc123", fingerprint);
    }

    [Fact]
    public void TheRankedChartPutsTheHighestScoreFirst()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes(Fixtures.Report));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-bar]")));
        var bars = cut.FindAll("[data-bar]").Select(bar => bar.GetAttribute("data-bar")).ToArray();
        Assert.Equal(new[] { "fake-b", "random" }, bars);
        Assert.NotEmpty(cut.FindAll("[data-section='chart'] svg[aria-label='Score per opponent']"));
    }

    [Fact]
    public void AnOpponentWithNoDecidedGameShowsDashesNotZeros()
    {
        // The run has just started: random has no decided game yet, so it has no score, Elo, RQC or legal rate to show.
        var report = Fixtures.Report.Replace("\"opponent\":\"random\",\"games\":2", "\"opponent\":\"random\",\"games\":0");
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes(report));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("table[data-testid='benchmark-opponents'] tbody tr").Count));
        var row = cut.FindAll("table[data-testid='benchmark-opponents'] tbody tr")[1];
        Assert.Equal("—", row.Children[6].TextContent);
        Assert.Equal("—", row.Children[7].TextContent);
        Assert.Equal("—", row.Children[8].TextContent);
        Assert.Equal("—", row.Children[9].TextContent);
        Assert.Equal(new[] { "fake-b" }, cut.FindAll("[data-bar]").Select(bar => bar.GetAttribute("data-bar")).ToArray());
    }

    [Fact]
    public void TheDownloadsCarryTheReportInTheLinkSoTheBrowserNeedsNoRequest()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes(Fixtures.Report));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='download-markdown']")));

        var markdown = cut.Find("[data-testid='download-markdown']");
        Assert.Equal("benchmark-spring-check.md", markdown.GetAttribute("download"));
        var markdownHref = markdown.GetAttribute("href")!;
        Assert.StartsWith("data:text/markdown;charset=utf-8,", markdownHref, StringComparison.Ordinal);
        Assert.Equal(Markdown, Uri.UnescapeDataString(markdownHref["data:text/markdown;charset=utf-8,".Length..]));

        var json = cut.Find("[data-testid='download-json']");
        Assert.Equal("benchmark-spring-check.json", json.GetAttribute("download"));
        var jsonHref = json.GetAttribute("href")!;
        Assert.StartsWith("data:application/json;charset=utf-8,", jsonHref, StringComparison.Ordinal);
        Assert.Contains("\"formulaVersion\": \"RATING-1\"", Uri.UnescapeDataString(jsonHref["data:application/json;charset=utf-8,".Length..]));
    }

    [Fact]
    public void ARunningBenchmarkShowsItsProgressAndCanBeCancelled()
    {
        using var ctx = new BunitContext();
        var report = Fixtures.Report.Replace("\"status\":\"finished\"", "\"status\":\"running\"");
        var cut = Render(ctx, Routes(report));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-section='progress']")));
        Assert.Contains("4 of 4 games", cut.Find("[data-section='progress']").TextContent);
        Assert.Single(cut.FindAll("button"), button => button.TextContent.Trim() == "Cancel benchmark");
    }

    [Fact]
    public void AFinishedBenchmarkHasNoProgressSection()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Routes(Fixtures.Report));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='benchmark-summary']")));
        Assert.Empty(cut.FindAll("[data-section='progress']"));
    }

    [Fact]
    public void AnUnknownBenchmarkSaysSoInsteadOfLoadingForever()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, new Dictionary<string, Reply>());

        cut.WaitForAssertion(() => Assert.Contains("No benchmark has this id.", cut.Markup));
    }
}
