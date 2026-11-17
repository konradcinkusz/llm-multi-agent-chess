using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chess.Analysis;

public static class AnalysisServiceCollectionExtensions
{
    /// <summary>Stockfish when a binary is configured or on PATH, otherwise the no-op analyzer.</summary>
    public static IHostApplicationBuilder AddMoveQualityAnalysis(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Analysis");
        var binary = StockfishLocator.Resolve(section["StockfishPath"], Environment.GetEnvironmentVariable("PATH"));
        var depth = section.GetValue("Depth", 12);
        var timeouts = new UciTimeouts(
            TimeSpan.FromSeconds(section.GetValue("HandshakeTimeoutSeconds", UciTimeouts.Default.Handshake.TotalSeconds)),
            TimeSpan.FromSeconds(section.GetValue("SearchTimeoutSeconds", UciTimeouts.Default.Search.TotalSeconds)),
            TimeSpan.FromSeconds(section.GetValue("RetryAfterSeconds", UciTimeouts.Default.RetryAfter.TotalSeconds)));

        builder.Services.AddSingleton<IMoveQualityAnalyzer>(_ => binary is null
            ? new NoOpMoveQualityAnalyzer()
            : new StockfishAnalyzer(binary, depth, timeouts));
        builder.Services.AddHealthChecks().AddCheck<MoveQualityHealthCheck>("stockfish");
        return builder;
    }
}
