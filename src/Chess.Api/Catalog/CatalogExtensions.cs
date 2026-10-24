namespace Chess.Api.Catalog;

public static class CatalogExtensions
{
    public static IHostApplicationBuilder AddCatalog(this IHostApplicationBuilder builder) => builder;
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
