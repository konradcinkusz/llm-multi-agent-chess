using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Attempts;

public static class AttemptExtensions
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static IHostApplicationBuilder AddAttempts(this IHostApplicationBuilder builder) => builder;

    /// <summary>Maps <c>GET /attempts</c> (relative to the <c>/api</c> group): the prompt/attempt log, newest first.</summary>
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/attempts", ListAttempts);
        return endpoints;
    }

    private static async Task<IResult> ListAttempts(
        IDbContextFactory<ArenaDbContext> factory,
        Guid? gameId,
        string? model,
        bool? legal,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        if (page < 1)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid paging",
                detail: "page must be 1 or greater.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid paging",
                detail: $"pageSize must be between 1 and {MaxPageSize}.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        // Attempts carry no model of their own: the side is given by the ply (ply 1 is White's first move).
        var attempts = db.MoveAttempts.AsNoTracking()
            .Join(db.Games.AsNoTracking(), a => a.GameId, g => g.Id, (a, g) => new { a, g });

        if (gameId is { } id)
        {
            attempts = attempts.Where(x => x.a.GameId == id);
        }

        if (legal is { } isLegal)
        {
            attempts = attempts.Where(x => x.a.Legal == isLegal);
        }

        if (model is not null)
        {
            attempts = attempts.Where(x =>
                (x.a.Ply % 2 == 1 && x.g.WhiteModel == model) || (x.a.Ply % 2 == 0 && x.g.BlackModel == model));
        }

        var total = await attempts.CountAsync(ct);

        // (page - 1) * pageSize can exceed int.MaxValue for a large page number; past the total there is nothing to read.
        var skip = (long)(page - 1) * pageSize;
        IReadOnlyList<AttemptDto> items = skip >= total
            ? []
            : await attempts
                .OrderByDescending(x => x.a.CreatedAt)
                .ThenByDescending(x => x.a.GameId)
                .ThenByDescending(x => x.a.Ply)
                .ThenByDescending(x => x.a.Number)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(x => new AttemptDto(
                    x.a.GameId, x.a.Ply, x.a.Number,
                    x.a.Ply % 2 == 1 ? x.g.WhiteModel : x.g.BlackModel,
                    x.a.Prompt, x.a.RawReply, x.a.ParsedSan, x.a.Legal, x.a.LatencyMs, x.a.Error, x.a.CreatedAt))
                .ToListAsync(ct);

        return TypedResults.Ok(new PageDto<AttemptDto>(items, page, pageSize, total));
    }
}
