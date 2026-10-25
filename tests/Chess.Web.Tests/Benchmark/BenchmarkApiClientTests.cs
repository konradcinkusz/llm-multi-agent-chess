using System.Net;
using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkApiClientTests
{
    private static readonly Guid Id = Guid.Parse(Fixtures.RunningId);

    [Fact]
    public async Task CreatePostsTheCandidateWithItsEffortAndReturnsTheRun()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["POST /api/benchmarks"] = Serve.Created($$"""{"benchmark":{{Fixtures.RunningBenchmark}},"estimatedPremiumRequests":[]}"""),
        });
        var client = new ArenaApiClient(Canned.Client(handler));

        var created = await client.CreateBenchmarkAsync(
            new CreateBenchmarkRequest("Spring check", "fake-a@high", ["fake-b", "random"], 2, 2));

        var sent = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement;
        Assert.Equal("fake-a@high", sent.GetProperty("candidate").GetString());
        Assert.Equal(2, sent.GetProperty("opponents").GetArrayLength());
        Assert.Equal(2, sent.GetProperty("gamesPerOpponent").GetInt32());
        Assert.Equal(Id, created.Benchmark.Id);
        Assert.Equal(4, created.Benchmark.GamesTotal);
    }

    [Fact]
    public async Task TheMarkdownReportIsReadAsText()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/benchmarks/44444444-4444-4444-4444-444444444444/report?format=md"] = new(HttpStatusCode.OK, Fixtures.Markdown),
        });
        var client = new ArenaApiClient(Canned.Client(handler));

        var markdown = await client.GetBenchmarkReportMarkdownAsync(Id);

        Assert.Equal(Fixtures.Markdown, markdown.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task TheReportDeserialisesIntoTheDtoShapes()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/benchmarks/44444444-4444-4444-4444-444444444444/report"] = Serve.Ok(Fixtures.Report),
        });
        var client = new ArenaApiClient(Canned.Client(handler));

        var report = await client.GetBenchmarkReportAsync(Id);

        Assert.Equal(1500, report.Summary.Rating);
        Assert.Equal("random", report.Opponents[1].Opponent);
        Assert.Null(report.Opponents[0].EloDiffHigh);
    }

    [Fact]
    public async Task CancelPostsToTheRunsCancelRoute()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["POST /api/benchmarks/44444444-4444-4444-4444-444444444444/cancel"] = Serve.Ok(Fixtures.RunningBenchmark),
        });
        var client = new ArenaApiClient(Canned.Client(handler));

        await client.CancelBenchmarkAsync(Id);

        Assert.Equal("POST", handler.Requests.Single().Method);
    }

    [Fact]
    public async Task AnUnknownRunIsAnHttpErrorTheCallerCanShow()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>());
        var client = new ArenaApiClient(Canned.Client(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBenchmarkReportAsync(Id));
    }
}
