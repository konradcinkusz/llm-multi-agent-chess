namespace Chess.Api.Attempts;

public static class AttemptExtensions
{
    public static IHostApplicationBuilder AddAttempts(this IHostApplicationBuilder builder) => builder;
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
