using Chess.Analysis;
using Chess.Api.Quality;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Quality;

public class MoveQualityServiceTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void Registration_adds_the_worker_with_the_thirty_second_default_interval()
    {
        var configured = app.WithWebHostBuilder(b => b
            .UseSetting("Database:Provider", "InMemory")
            .UseSetting("Llm:Provider", "Fake"));

        using var scope = configured.Services.CreateScope();
        var worker = scope.ServiceProvider.GetServices<IHostedService>().OfType<MoveQualityService>().Single();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MoveQualityOptions>>().Value;

        Assert.NotNull(worker);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Interval);
    }

    [Fact]
    public async Task Worker_returns_at_once_when_the_analyzer_is_unavailable()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, game, Start, ["e4", "e5"], QualitySeed.T0.AddMinutes(5));
        var analyzer = new StubMoveQualityAnalyzer { IsAvailable = false };
        var worker = Worker(name, analyzer, TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);

        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, worker.ExecuteTask.Status);
        await worker.StopAsync(CancellationToken.None);
        Assert.Empty(analyzer.AnalyzedStartFens);
        Assert.All((await QualitySeed.LoadMovesAsync(name)).Values, m => Assert.Null(m.CentipawnLoss));
    }

    [Fact]
    public async Task Worker_annotates_a_finished_game_on_a_tick()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, game, Start, ["e4", "e5", "Nf3"], QualitySeed.T0.AddMinutes(5));
        var worker = Worker(name, new StubMoveQualityAnalyzer(), TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline
                   && (await QualitySeed.LoadMovesAsync(name)).Values.Any(m => m.CentipawnLoss is null))
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var moves = await QualitySeed.LoadMovesAsync(name);
        Assert.Equal(3, moves.Count);
        Assert.All(moves.Values, m => Assert.NotNull(m.CentipawnLoss));
        Assert.All(moves.Values, m => Assert.NotNull(m.BestSan));
    }

    private static MoveQualityService Worker(string databaseName, IMoveQualityAnalyzer analyzer, TimeSpan interval)
    {
        var pipeline = new MoveQualityPipeline(
            new QualityFactory(databaseName), analyzer, NullLogger<MoveQualityPipeline>.Instance);
        return new MoveQualityService(
            pipeline,
            analyzer,
            TimeProvider.System,
            Options.Create(new MoveQualityOptions { Interval = interval }),
            NullLogger<MoveQualityService>.Instance);
    }
}
