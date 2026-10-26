using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Chess.Api.Usage;
using Chess.Engine;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Broadcast;

/// <summary>A paid request that has its game: the game to play, and the budget that stops it.</summary>
public sealed record RequestGame(Guid GameId, IGameBudget Budget);

/// <summary>
/// What the broadcast runner needs to play a paid request (03 §15.1, §15.2): the request is taken from the queue, refused and
/// credited when a side is not a bring-your-own-key model, and otherwise becomes a rated game with a budget. The queue keeps
/// its own rules (a failure is requeued once, then credited); the refusal is the one case that credits at once.
/// </summary>
public sealed class BroadcastRequests(
    IDbContextFactory<ArenaDbContext> factory,
    IGameRequestQueue queue,
    ModelCatalog catalog,
    IOptions<ArenaOptions> arena,
    IOptions<RequestOptions> requests,
    IOptions<CatalogOptions> catalogOptions,
    TimeProvider clock)
{
    /// <summary>The reason a request is refused when a side is not a custom model (03 §15.2).</summary>
    public const string NotCustomReason = "not a bring-your-own-key model; credit kept";

    public Task<QueuedRequest?> TakeNextAsync(CancellationToken ct) => queue.TakeNextAsync(ct);

    public Task PlayedAsync(Guid requestId, CancellationToken ct) => queue.PlayedAsync(requestId, ct);

    public Task FailedAsync(Guid requestId, string reason, CancellationToken ct) => queue.FailedAsync(requestId, reason, ct);

    /// <summary>
    /// Creates the request's game, linked to the request, or refuses the request and credits it. Returns null when refused.
    /// </summary>
    public async Task<RequestGame?> StartAsync(QueuedRequest request, CancellationToken ct)
    {
        if (!AllCustom(request))
        {
            await FailAndCreditAsync(request.Id, NotCustomReason, ct);
            return null;
        }

        var listing = await catalog.GetModelsAsync(ct);
        var whitePricing = PricingOf(listing.Models, request.White);
        var blackPricing = PricingOf(listing.Models, request.Black);

        var start = ChessBoard.StartingPosition().ToFen();
        var game = new GameEntity
        {
            Id = Guid.NewGuid(),
            WhiteKind = "llm",
            WhiteModel = request.White,
            BlackKind = "llm",
            BlackModel = request.Black,
            Rated = true,
            Result = "*",
            StartFen = start,
            CurrentFen = start,
            CreatedAt = clock.GetUtcNow(),
        };

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Games.Add(game);
            await db.SaveChangesAsync(ct);
        }

        await queue.StartedAsync(request.Id, game.Id, ct);
        var budget = new RequestBudget(factory, request.White, request.Black, whitePricing, blackPricing,
            request.BudgetUsd, catalogOptions.Value.Cost.UsdPerAiCredit);
        return new RequestGame(game.Id, budget);
    }

    /// <summary>True when both sides are custom models. Random and Copilot models are not, so they are refused (03 §15.2).</summary>
    private bool AllCustom(QueuedRequest request)
    {
        if (!requests.Value.RequireApiKeyModels)
        {
            return true;
        }

        var custom = arena.Value.CustomModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return custom.Contains(ModelVariant.BaseOf(request.White)) && custom.Contains(ModelVariant.BaseOf(request.Black));
    }

    private static ModelPricingDto? PricingOf(IReadOnlyList<ModelDto> models, string playerId) =>
        models.FirstOrDefault(m => m.Id == ModelVariant.BaseOf(playerId))?.Pricing;

    /// <summary>
    /// Fails a request that is playing and credits it, at once and without a requeue. Only a playing request changes, so a
    /// request that another path already closed is left as it is.
    /// </summary>
    private async Task FailAndCreditAsync(Guid requestId, string reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.GameRequests.FirstAsync(r => r.Id == requestId, ct);
        if (request.Status != RequestStatus.Playing)
        {
            return;
        }

        var now = clock.GetUtcNow();
        request.Status = RequestStatus.Failed;
        request.Reason = reason;
        request.FinishedAt = now;
        await PayerLedger.CreditAsync(db, request, now, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The running cost of one request game, in US dollars, from its stored attempts (03 §15.2). Asked after every move; the
    /// game is over budget once its cost reaches the net amount the owner received. A side with no price counts as 0.
    /// </summary>
    private sealed class RequestBudget(
        IDbContextFactory<ArenaDbContext> factory,
        string whiteModel,
        string blackModel,
        ModelPricingDto? whitePricing,
        ModelPricingDto? blackPricing,
        double budgetUsd,
        double usdPerAiCredit) : IGameBudget
    {
        public async Task<bool> IsExhaustedAsync(Guid id, CancellationToken ct)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var attempts = await db.MoveAttempts.AsNoTracking()
                .Where(a => a.GameId == id)
                .Select(a => new AttemptRow(a.Ply, a.InputTokens, a.OutputTokens, a.ReasoningTokens, a.CachedInputTokens, a.AiCredits))
                .ToListAsync(ct);

            // Attempts carry no side: ply 1 is White's first move, so odd plies are White's.
            var white = SideCost("white", whiteModel, whitePricing, attempts.Where(a => a.Ply % 2 == 1));
            var black = SideCost("black", blackModel, blackPricing, attempts.Where(a => a.Ply % 2 == 0));
            return white + black >= budgetUsd;
        }

        private double SideCost(string color, string model, ModelPricingDto? pricing, IEnumerable<AttemptRow> attempts) =>
            GameCostCalculator.ForSide(color, model, true,
                attempts.Select(a => new GameCostCalculator.ReplyUsage(a.InputTokens, a.OutputTokens, a.ReasoningTokens,
                    a.CachedInputTokens, a.AiCredits)).ToList(),
                pricing, usdPerAiCredit).EstimatedUsd ?? 0;

        private sealed record AttemptRow(int Ply, int? InputTokens, int? OutputTokens, int? ReasoningTokens,
            int? CachedInputTokens, double? AiCredits);
    }
}
