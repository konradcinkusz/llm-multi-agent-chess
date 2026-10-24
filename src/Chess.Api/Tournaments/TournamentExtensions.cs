namespace Chess.Api.Tournaments;

public static class TournamentExtensions
{
    public static IHostApplicationBuilder AddTournaments(this IHostApplicationBuilder builder) => builder;
    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
