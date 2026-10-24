using Chess.Api.Contracts;
using Chess.Api.Games;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tournaments;

public static class TournamentExtensions
{
    /// <summary>
    /// Registers the tournament service and the scheduler that starts its games. Needs the game runner, persistence and
    /// catalog registrations, which are added first.
    /// </summary>
    public static IHostApplicationBuilder AddTournaments(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<TournamentService>();
        builder.Services.AddHostedService<TournamentScheduler>();
        return builder;
    }

    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/tournaments", async Task<Results<Created<TournamentCreatedDto>, ValidationProblem>> (
            CreateTournamentRequest request,
            TournamentService tournaments,
            IOptions<GameRunnerOptions> limits,
            CancellationToken ct) =>
        {
            var errors = TournamentRules.Validate(request, limits.Value.MaxConcurrentGames);
            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var created = await tournaments.CreateAsync(request, ct);
            return TypedResults.Created($"/api/tournaments/{created.Tournament.Id}", created);
        });

        endpoints.MapGet("/tournaments", (TournamentService tournaments, CancellationToken ct) =>
            tournaments.ListAsync(ct));

        endpoints.MapGet("/tournaments/{id:guid}", async Task<Results<Ok<TournamentDto>, ProblemHttpResult>> (
            Guid id, TournamentService tournaments, CancellationToken ct) =>
        {
            var tournament = await tournaments.GetAsync(id, ct);
            if (tournament is null)
            {
                return NotFound(id);
            }

            return TypedResults.Ok(tournament);
        });

        endpoints.MapDelete("/tournaments/{id:guid}", async Task<Results<Ok<TournamentDto>, ProblemHttpResult>> (
            Guid id, TournamentService tournaments, CancellationToken ct) =>
        {
            var tournament = await tournaments.CancelAsync(id, ct);
            if (tournament is null)
            {
                return NotFound(id);
            }

            return TypedResults.Ok(tournament);
        });

        return endpoints;
    }

    private static ProblemHttpResult NotFound(Guid id) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Tournament not found",
        detail: $"No tournament has the id {id}.");
}
