using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

// Chess.Api.Results is a namespace, so the framework's Results class needs an alias here.
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Chess.Api.Ratings;

public static class RatingExtensions
{
    public static IHostApplicationBuilder AddRatings(this IHostApplicationBuilder builder) => builder;

    /// <summary>
    /// <c>GET /api/ratings</c>, <c>/api/ratings/pairwise?a&amp;b</c> and <c>/api/ratings/matrix</c>. Each computes
    /// from the games table on every request (no caching) and takes an optional <c>tournamentId</c> to restrict the games.
    /// <c>/api/ratings</c> also takes <c>order=score|conservative</c> (default <c>score</c>, RATING.md §6).
    /// </summary>
    public static IEndpointRouteBuilder MapRatingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ratings", async (IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, string? order, CancellationToken ct) =>
        {
            if (!TryParseOrder(order, out var ranking))
            {
                return HttpResults.Problem(
                    title: "Unknown order",
                    detail: $"Order must be 'score' or 'conservative', was '{order}'.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (_, report) = await ComputeAsync(factory, tournamentId, ct);
            var ordered = RatingCalculator.Order(report, ranking);
            return HttpResults.Ok(new LeaderboardDto(
                ordered.Models,
                ordered.WhiteAdvantage,
                ordered.Games,
                ordered.Models.Any(m => m.MoveQuality is not null)));
        });

        endpoints.MapGet("/ratings/pairwise", async (string a, string b, IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct) =>
        {
            var (rated, report) = await ComputeAsync(factory, tournamentId, ct);
            var unknown = new[] { a, b }.FirstOrDefault(model => report.Models.All(m => m.Model != model));
            if (unknown is not null)
            {
                return HttpResults.Problem(
                    title: "Model not rated",
                    detail: $"Model '{unknown}' has no finished rated games.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return HttpResults.Ok(RatingCalculator.Pairwise(report, rated.Outcomes, a, b));
        });

        // Every ordered pair of distinct models, in leaderboard order. The diagonal is left out: RQC(A, A) is 0.5 by definition.
        endpoints.MapGet("/ratings/matrix", async (IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct) =>
        {
            var (rated, report) = await ComputeAsync(factory, tournamentId, ct);
            var models = report.Models.Select(m => m.Model).ToList();
            var cells = models
                .SelectMany(a => models.Where(b => b != a).Select(b => RatingCalculator.Pairwise(report, rated.Outcomes, a, b)))
                .ToList();
            return HttpResults.Ok(new MatrixDto(models, cells));
        });

        return endpoints;
    }

    // A missing order is the default (score). Anything other than the two names is a client error, not a fallback.
    private static bool TryParseOrder(string? value, out RatingOrder order)
    {
        switch (value)
        {
            case null or "score":
                order = RatingOrder.Score;
                return true;
            case "conservative":
                order = RatingOrder.Conservative;
                return true;
            default:
                order = default;
                return false;
        }
    }

    private static async Task<(RatedGames Rated, RatingReport Report)> ComputeAsync(
        IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct)
    {
        var rated = await RatedGameLoader.LoadAsync(factory, tournamentId, ct);
        var report = RatingCalculator.Compute(rated.Outcomes, rated.MoveStats, new RatingOptions());
        return (rated, report);
    }
}
