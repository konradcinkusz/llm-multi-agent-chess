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

        builder.Services.AddSingleton<IMoveQualityAnalyzer>(_ => binary is null
            ? new NoOpMoveQualityAnalyzer()
            : new StockfishAnalyzer(binary, depth));
        builder.Services.AddHealthChecks().AddCheck<MoveQualityHealthCheck>("stockfish");
        return builder;
    }
}
