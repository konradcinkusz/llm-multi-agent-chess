using Microsoft.Extensions.Hosting;

namespace Chess.Analysis;

public static class AnalysisServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddMoveQualityAnalysis(this IHostApplicationBuilder builder) => builder;
}
