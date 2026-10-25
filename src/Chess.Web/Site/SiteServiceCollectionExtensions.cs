using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Web.Site;

public static class SiteServiceCollectionExtensions
{
    /// <summary>Registers the static site exporter and binds <c>Arena:Site</c> (03 §11).</summary>
    public static IServiceCollection AddSiteExport(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SiteOptions>().Bind(configuration.GetSection("Arena:Site"));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISiteExporter, SiteExporter>();
        return services;
    }
}
