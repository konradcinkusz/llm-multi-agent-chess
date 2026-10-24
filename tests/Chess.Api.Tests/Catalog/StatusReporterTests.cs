using Chess.Api.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Chess.Api.Tests.Catalog;

public class StatusReporterTests
{
    [Fact]
    public async Task ReadsEachCheckByNameAndCarriesCopilotDescription()
    {
        var reporter = Reporter(checks =>
        {
            checks.AddCheck("copilot", () => HealthCheckResult.Degraded("Sign in with GitHub Copilot."));
            checks.AddCheck("database", () => HealthCheckResult.Healthy("connected"));
            checks.AddCheck("stockfish", () => HealthCheckResult.Unhealthy("binary not found"));
        });

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal("degraded", status.Copilot);
        Assert.Equal("Sign in with GitHub Copilot.", status.CopilotDetail);
        Assert.Equal("healthy", status.Database);
        Assert.Equal("unhealthy", status.Stockfish);
    }

    [Fact]
    public async Task ChecksThatAreNotRegisteredAreReportedAsUnknown()
    {
        var reporter = Reporter(checks => checks.AddCheck("copilot", () => HealthCheckResult.Healthy("Fake gateway")));

        var status = await reporter.GetStatusAsync(default);

        Assert.Equal("healthy", status.Copilot);
        Assert.Equal("Fake gateway", status.CopilotDetail);
        Assert.Equal("unknown", status.Database);
        Assert.Equal("unknown", status.Stockfish);
    }

    [Fact]
    public async Task NoChecksAtAllReportsEverythingUnknown()
    {
        var status = await Reporter(_ => { }).GetStatusAsync(default);

        Assert.Equal(new("unknown", "unknown", "unknown", "unknown"), status);
    }

    private static StatusReporter Reporter(Action<IHealthChecksBuilder> checks)
    {
        var services = new ServiceCollection().AddOptions().AddLogging();
        checks(services.AddHealthChecks());
        return new StatusReporter(services.BuildServiceProvider().GetRequiredService<HealthCheckService>());
    }
}
