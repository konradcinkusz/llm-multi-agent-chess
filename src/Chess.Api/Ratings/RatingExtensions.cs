using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

// Chess.Api.Results is a namespace, so the framework's Results class needs an alias here.
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Chess.Api.Ratings;

public static class RatingExtensions
{
    public static IHostApplicationBuilder AddRatings(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<RatingMemo>();
        return builder;
    }

    /// <summary>
    /// <c>GET /api/ratings</c>, <c>/api/ratings/pairwise?a&amp;b</c> and <c>/api/ratings/matrix</c>. Each takes an optional
    /// <c>tournamentId</c> to restrict the games and answers from the games table: <see cref="RatingMemo"/> remembers the
    /// computation per scope and recomputes when the rated games or their move annotations change, so the numbers are
    /// always those of the data as it is now. The matrix and the pairwise endpoint read the same cells.
    /// <c>/api/ratings</c> also takes <c>order=score|conservative</c> (default <c>score</c>, RATING.md §6).
    /// </summary>
    public static IEndpointRouteBuilder MapRatingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ratings", async (IDbContextFactory<ArenaDbContext> factory, RatingMemo memo, Guid? tournamentId, string? order, CancellationToken ct) =>
        {
            if (!TryParseOrder(order, out var ranking))
            {
                return HttpResults.Problem(
                    title: "Unknown order",
                    detail: $"Order must be 'score' or 'conservative', was '{order}'.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var view = await memo.GetAsync(factory, tournamentId, ct);
            var ordered = RatingCalculator.Order(view.Report, ranking);
            return HttpResults.Ok(new LeaderboardDto(
                ordered.Models,
                ordered.WhiteAdvantage,
                ordered.Games,
                ordered.Models.Any(m => m.MoveQuality is not null)));
        });

        endpoints.MapGet("/ratings/pairwise", async (string a, string b, IDbContextFactory<ArenaDbContext> factory, RatingMemo memo, Guid? tournamentId, CancellationToken ct) =>
        {
            var view = await memo.GetAsync(factory, tournamentId, ct);
            var unknown = new[] { a, b }.FirstOrDefault(model => view.Report.Models.All(m => m.Model != model));
            if (unknown is not null)
            {
                return HttpResults.Problem(
                    title: "Model not rated",
                    detail: $"Model '{unknown}' has no finished rated games.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // The matrix holds the cell of every pair of distinct models, numerically the same as Pairwise. A model
            // against itself is not in it, so that rare request is computed directly.
            var cell = view.Matrix.FirstOrDefault(c => c.A == a && c.B == b);
            return HttpResults.Ok(cell ?? RatingCalculator.Pairwise(view.Report, view.Rated.Outcomes, a, b));
        });

        // Every ordered pair of distinct models, in leaderboard order. The diagonal is left out: RQC(A, A) is 0.5 by definition.
        endpoints.MapGet("/ratings/matrix", async (IDbContextFactory<ArenaDbContext> factory, RatingMemo memo, Guid? tournamentId, CancellationToken ct) =>
        {
            var view = await memo.GetAsync(factory, tournamentId, ct);
            return HttpResults.Ok(new MatrixDto(view.Report.Models.Select(m => m.Model).ToList(), view.Matrix));
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
}
