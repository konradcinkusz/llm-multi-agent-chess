using Chess.Api.Players.Prompts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Catalog;

public static class CatalogExtensions
{
    public static IHostApplicationBuilder AddCatalog(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<CatalogOptions>()
            .BindConfiguration("Catalog")
            .Configure<IConfiguration>((options, configuration) => configuration.GetSection("Arena").Bind(options));
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ModelCatalog>();
        builder.Services.AddSingleton<StatusReporter>();
        return builder;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/models", async (ModelCatalog catalog, HttpResponse response, CancellationToken ct) =>
        {
            var listing = await catalog.GetModelsAsync(ct);
            if (listing.Warning is not null)
            {
                response.Headers["Warning"] = $"299 Chess.Api \"{listing.Warning}\"";
            }

            return listing.Models;
        });

        endpoints.MapGet("/status", (StatusReporter status, CancellationToken ct) => status.GetStatusAsync(ct));

        // The styles in their order: classic first (03 §14.3). Each carries the version of its texts.
        endpoints.MapGet("/prompt-styles", () => PromptStyles.All.Select(style => style.ToDto()).ToArray());

        return endpoints;
    }
}
