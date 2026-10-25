using Chess.Api.Catalog;
using Chess.Api.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Chess.Api.Results;

public static class ResultsExtensions
{
    public static IHostApplicationBuilder AddResults(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<RatingSnapshotOptions>().BindConfiguration("RatingSnapshots");
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<RatingSnapshotService>();
        return builder;
    }

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ratings/history", RatingHistoryHandler);
        endpoints.MapGet("/results/breakdown", ResultsBreakdownHandler);
        return endpoints;
    }

    private static async Task<IResult> RatingHistoryHandler(
        string? model, IDbContextFactory<ArenaDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return TypedResults.Ok(await RatingHistory.ReadAsync(db, model, ct));
    }

    private static async Task<IResult> ResultsBreakdownHandler(
        Guid? tournamentId,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        IOptions<CatalogOptions> options,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (tournamentId is { } id && !await db.Tournaments.AnyAsync(t => t.Id == id, ct))
        {
            return TypedResults.Problem(
                title: "Tournament not found",
                detail: $"No tournament with id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // The catalog's prices turn tokens into an estimated cost (03 §10.2); without a ready gateway no model has a price.
        var listing = await catalog.GetModelsAsync(ct);
        return TypedResults.Ok(await ResultsBreakdown.ReadAsync(db, tournamentId, listing.Models,
            options.Value.Cost.UsdPerAiCredit, ct));
    }
}
