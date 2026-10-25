using Chess.Analysis;
using Chess.Api.Catalog;
using Chess.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Chess.Api.Tests.Catalog;

public class StatusReporterTests
{
    [Theory]
    [InlineData(LlmGatewayState.Ready, "Ready")]
    [InlineData(LlmGatewayState.Fake, "Fake")]
    [InlineData(LlmGatewayState.NotAuthenticated, "NotAuthenticated")]
    [InlineData(LlmGatewayState.Unavailable, "Unavailable")]
    public async Task CopilotIsTheGatewayStateNotTheHealthCheckVerdict(LlmGatewayState state, string expected)
    {
        var reporter = Reporter(_ => { }, new LlmGatewayStatus(state, "detail text"));

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal(expected, status.Copilot);
        Assert.Equal("detail text", status.CopilotDetail);
    }

    [Fact]
    public async Task ReadsDatabaseAndStockfishByName()
    {
        var reporter = Reporter(checks =>
        {
            checks.AddCheck("database", () => HealthCheckResult.Healthy("connected"));
            checks.AddCheck("stockfish", () => HealthCheckResult.Unhealthy("binary not found"));
        });

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal("healthy", status.Database);
        Assert.Equal("unhealthy", status.Stockfish);
    }

    [Fact]
    public async Task ChecksThatAreNotRegisteredAreReportedAsUnknown()
    {
        var status = await Reporter(_ => { }).GetStatusAsync(default);

        Assert.Equal("unknown", status.Database);
        Assert.Equal("unknown", status.Stockfish);
    }

    [Theory]
    [InlineData(true, "available")]
    [InlineData(false, "absent")]
    public async Task StockfishIsAvailableOrAbsentWhenItsRealCheckIsHealthy(bool analyzerAvailable, string expected)
    {
        // The real check: absent is Healthy too, so only its description tells the two apart.
        IMoveQualityAnalyzer analyzer = analyzerAvailable ? new FixedAnalyzer(available: true) : new NoOpMoveQualityAnalyzer();
        var reporter = Reporter(checks => checks.AddCheck<MoveQualityHealthCheck>("stockfish"), analyzer: analyzer);

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal(expected, status.Stockfish);
    }

    [Fact]
    public async Task AbsentStockfishIsNotReportedAsHealthy()
    {
        var reporter = Reporter(checks => checks.AddCheck<MoveQualityHealthCheck>("stockfish"), analyzer: new NoOpMoveQualityAnalyzer());

        var status = await reporter.GetStatusAsync(default);

        Assert.NotEqual("healthy", status.Stockfish);
    }

    [Theory]
    [InlineData("degraded")]
    [InlineData("unhealthy")]
    public async Task OtherStockfishVerdictsPassThroughUnchanged(string verdict)
    {
        var reporter = Reporter(checks => checks.AddCheck("stockfish", () => verdict switch
        {
            "degraded" => HealthCheckResult.Degraded("engine slow to answer"),
            _ => HealthCheckResult.Unhealthy("engine crashed"),
        }));

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal(verdict, status.Stockfish);
    }

    [Fact]
    public async Task DatabaseIsNeverMappedToAvailableOrAbsent()
    {
        var reporter = Reporter(checks => checks.AddCheck("database", () => HealthCheckResult.Healthy("absent — still healthy")));

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal("healthy", status.Database);
    }

    private static StatusReporter Reporter(
        Action<IHealthChecksBuilder> checks,
        LlmGatewayStatus? gateway = null,
        IMoveQualityAnalyzer? analyzer = null)
    {
        var services = new ServiceCollection().AddOptions().AddLogging();
        services.AddSingleton(analyzer ?? new NoOpMoveQualityAnalyzer());
        checks(services.AddHealthChecks());
        return new StatusReporter(
            services.BuildServiceProvider().GetRequiredService<HealthCheckService>(),
            new StubGateway(gateway ?? new LlmGatewayStatus(LlmGatewayState.Ready, "ok")));
    }

    /// <summary>An analyzer whose only answer is whether it is available; it never analyses anything.</summary>
    private sealed class FixedAnalyzer(bool available) : IMoveQualityAnalyzer
    {
        public bool IsAvailable => available;

        public Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MoveQuality>>([]);

        public Task<int?> EvaluateAsync(string fen, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    private sealed class StubGateway(LlmGatewayStatus status) : ILlmGateway
    {
        public LlmGatewayStatus Status { get; } = status;

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);

        public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
