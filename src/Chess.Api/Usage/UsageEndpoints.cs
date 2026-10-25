using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Usage;

public static class UsageExtensions
{
    /// <summary>Maps <c>GET /games/{id}/usage</c> (relative to the <c>/api</c> group): the game's tokens and cost per side.</summary>
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/games/{id:guid}/usage", GetUsageAsync);
        return endpoints;
    }

    private const string LlmKind = "llm";

    private static async Task<IResult> GetUsageAsync(
        Guid id,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        IOptions<CatalogOptions> options,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new { g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel })
            .FirstOrDefaultAsync(ct);
        if (game is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Game not found",
                detail: $"No game has the id {id}.");
        }

        var attempts = await db.MoveAttempts.AsNoTracking()
            .Where(a => a.GameId == id)
            .Select(a => new AttemptRow(a.Ply, a.InputTokens, a.OutputTokens, a.ReasoningTokens, a.CachedInputTokens, a.AiCredits))
            .ToListAsync(ct);

        // Attempts carry no side of their own: ply 1 is White's first move, so odd plies are White's.
        var listing = await catalog.GetModelsAsync(ct);
        var rate = options.Value.Cost.UsdPerAiCredit;
        var white = GameCostCalculator.ForSide("white", game.WhiteModel, game.WhiteKind == LlmKind,
            RepliesOf(attempts, ply => ply % 2 == 1), PricesOf(listing.Models, game.WhiteModel), rate);
        var black = GameCostCalculator.ForSide("black", game.BlackModel, game.BlackKind == LlmKind,
            RepliesOf(attempts, ply => ply % 2 == 0), PricesOf(listing.Models, game.BlackModel), rate);

        return TypedResults.Ok(GameCostCalculator.ForGame(id, white, black, rate));
    }

    private static IReadOnlyList<GameCostCalculator.ReplyUsage> RepliesOf(IEnumerable<AttemptRow> attempts, Func<int, bool> sideOfPly) =>
        attempts.Where(a => sideOfPly(a.Ply))
            .Select(a => new GameCostCalculator.ReplyUsage(a.InputTokens, a.OutputTokens, a.ReasoningTokens,
                a.CachedInputTokens, a.AiCredits))
            .ToList();

    private static ModelPricingDto? PricesOf(IReadOnlyList<ModelDto> models, string modelId) =>
        models.FirstOrDefault(m => m.Id == modelId)?.Pricing;

    /// <summary>The columns of one attempt that the cost needs.</summary>
    private sealed record AttemptRow(int Ply, int? InputTokens, int? OutputTokens, int? ReasoningTokens,
        int? CachedInputTokens, double? AiCredits);
}
