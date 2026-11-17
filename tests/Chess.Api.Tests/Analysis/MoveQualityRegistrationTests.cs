using Chess.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class MoveQualityRegistrationTests
{
    [Fact]
    public async Task Missing_binary_registers_the_no_op_analyzer_and_a_healthy_absent_check()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analysis:StockfishPath"] = Path.Combine(Path.GetTempPath(), "no-such-stockfish-binary"),
        });
        builder.AddMoveQualityAnalysis();
        using var host = builder.Build();

        Assert.IsType<NoOpMoveQualityAnalyzer>(host.Services.GetRequiredService<IMoveQualityAnalyzer>());

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, report.Entries["stockfish"].Status);
        Assert.Equal("absent — move quality disabled", report.Entries["stockfish"].Description);
    }

    [Fact]
    public async Task A_configured_binary_that_does_not_work_registers_stockfish_and_a_degraded_check()
    {
        // An empty file is found but cannot run: the check says so (Degraded, never Unhealthy) and move quality stays off.
        var binary = Path.GetTempFileName();
        try
        {
            using var host = Build(binary);

            var analyzer = Assert.IsType<StockfishAnalyzer>(host.Services.GetRequiredService<IMoveQualityAnalyzer>());
            Assert.True(analyzer.IsAvailable, "an engine that has not been tried is not known to be broken");

            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Degraded, report.Entries["stockfish"].Status);
            Assert.StartsWith("binary does not work", report.Entries["stockfish"].Description);
            Assert.False(analyzer.IsAvailable);
        }
        finally
        {
            File.Delete(binary);
        }
    }

    [UnixFact]
    public async Task A_configured_binary_that_answers_the_handshake_registers_an_available_check()
    {
        using var binary = FakeUciBinary.Working();
        using var host = Build(binary.Path);

        Assert.IsType<StockfishAnalyzer>(host.Services.GetRequiredService<IMoveQualityAnalyzer>());

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, report.Entries["stockfish"].Status);
        Assert.Equal("available", report.Entries["stockfish"].Description);
    }

    [UnixFact]
    public async Task A_configured_binary_that_never_answers_is_degraded_after_the_configured_handshake_timeout()
    {
        using var binary = FakeUciBinary.Silent();
        using var host = Build(binary.Path, ("Analysis:HandshakeTimeoutSeconds", "0.4"));

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, report.Entries["stockfish"].Status);
        Assert.Contains("UCI handshake", report.Entries["stockfish"].Description);
    }

    private static IHost Build(string binary, params (string Key, string Value)[] more)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var settings = new Dictionary<string, string?>
        {
            ["Analysis:StockfishPath"] = binary,
            ["Analysis:Depth"] = "8",
        };
        foreach (var (key, value) in more)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddMoveQualityAnalysis();
        return builder.Build();
    }
}
