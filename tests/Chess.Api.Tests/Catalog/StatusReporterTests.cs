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

    private static StatusReporter Reporter(Action<IHealthChecksBuilder> checks, LlmGatewayStatus? gateway = null)
    {
        var services = new ServiceCollection().AddOptions().AddLogging();
        checks(services.AddHealthChecks());
        return new StatusReporter(
            services.BuildServiceProvider().GetRequiredService<HealthCheckService>(),
            new StubGateway(gateway ?? new LlmGatewayStatus(LlmGatewayState.Ready, "ok")));
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
