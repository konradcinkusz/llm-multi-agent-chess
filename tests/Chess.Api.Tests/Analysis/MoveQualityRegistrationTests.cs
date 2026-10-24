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
    public async Task Configured_binary_registers_stockfish_and_an_available_check()
    {
        var binary = Path.GetTempFileName();
        try
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analysis:StockfishPath"] = binary,
                ["Analysis:Depth"] = "8",
            });
            builder.AddMoveQualityAnalysis();
            using var host = builder.Build();

            Assert.IsType<StockfishAnalyzer>(host.Services.GetRequiredService<IMoveQualityAnalyzer>());

            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, report.Entries["stockfish"].Status);
            Assert.Equal("available", report.Entries["stockfish"].Description);
        }
        finally
        {
            File.Delete(binary);
        }
    }
}
