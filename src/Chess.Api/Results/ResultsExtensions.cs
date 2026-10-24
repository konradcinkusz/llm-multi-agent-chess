namespace Chess.Api.Results;

public static class ResultsExtensions
{
    public static IHostApplicationBuilder AddResults(this IHostApplicationBuilder builder) => builder;
    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
