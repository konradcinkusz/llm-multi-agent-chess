using Chess.Api.Catalog;
using Chess.Api.Persistence;
using Chess.Api.Results.Archive;
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
        builder.Services.AddOptions<ArchiveOptions>().BindConfiguration("Arena:Results:Archive");
        builder.Services.AddSingleton<ResultsArchive>();
        builder.Services.AddSingleton<ArchiveImportStatus>();
        builder.Services.AddHostedService<ArchiveImportService>();
        return builder;
    }

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ratings/history", RatingHistoryHandler);
        endpoints.MapGet("/results/breakdown", ResultsBreakdownHandler);
        endpoints.MapPost("/results/archive/export", ArchiveExportHandler);
        endpoints.MapGet("/results/archive/status", (ArchiveImportStatus status) => TypedResults.Ok(new { status.State, status.Added }));
        return endpoints;
    }

    private static async Task<IResult> RatingHistoryHandler(
        string? model, IDbContextFactory<ArenaDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return TypedResults.Ok(await RatingHistory.ReadAsync(db, model, ct));
    }

    /// <summary>Writes the rated games that have no file yet into <c>Arena:Results:Archive:ExportFolder</c> (docs/RESULTS-ARCHIVE.md).</summary>
    private static async Task<IResult> ArchiveExportHandler(
        ResultsArchive archive, IOptions<ArchiveOptions> options, CancellationToken ct)
    {
        if (options.Value.ExportFolder is not { Length: > 0 } folder)
        {
            return TypedResults.Problem(
                title: "No export folder",
                detail: "Set Arena:Results:Archive:ExportFolder to the results/ folder of a clone of the repository.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(await archive.ExportAsync(folder, ct));
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
