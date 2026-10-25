using Chess.Web.Site;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

public static class SitePublishingServices
{
    /// <summary>
    /// Registers publishing (03 §11): the Arena:Site options, the git publisher, the export coordinator and auto-publish.
    /// ISiteExporter falls back to NullSiteExporter until the exporter is registered; an exporter registered by anyone else
    /// wins, whatever the order of the registrations.
    /// </summary>
    public static IServiceCollection AddSitePublishing(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<SitePublishingOptions>(configuration.GetSection(SitePublishingOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISiteExporter, NullSiteExporter>();
        services.AddSingleton<ISitePublisher>(sp => new GitSitePublisher(
            sp.GetRequiredService<IOptions<SitePublishingOptions>>(), sp.GetRequiredService<TimeProvider>(), environment.ContentRootPath));
        services.AddTransient<ISiteActivity, ArenaSiteActivity>();
        services.AddSingleton<SiteExportCoordinator>();
        services.AddHostedService<SiteAutoPublisher>();
        return services;
    }
}
