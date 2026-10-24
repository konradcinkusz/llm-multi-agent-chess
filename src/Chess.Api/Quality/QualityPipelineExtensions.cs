using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Quality;

public static class QualityPipelineExtensions
{
    /// <summary>
    /// Registers the post-game move-quality worker (T2.8) and the <c>MoveQuality</c> options. Needs the persistence
    /// and analysis registrations, which are added first.
    /// </summary>
    public static IHostApplicationBuilder AddMoveQualityPipeline(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<MoveQualityOptions>().BindConfiguration("MoveQuality");
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MoveQualityPipeline>();
        builder.Services.AddHostedService<MoveQualityService>();
        return builder;
    }
}
