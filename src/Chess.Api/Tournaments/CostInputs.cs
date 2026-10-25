using Chess.Api.Catalog;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tournaments;

/// <summary>
/// Gathers the inputs of the cost estimates: the rate of an AI credit (<c>Arena:Cost</c>, bound by the catalog), the catalog's
/// prices and premium multipliers, and the token averages over the stored moves. Tournaments, the broadcast planner and
/// <c>GET /api/cost/inputs</c> all read through this.
/// </summary>
public sealed class CostInputs(IDbContextFactory<ArenaDbContext> factory, IOptions<CatalogOptions> catalog)
{
    /// <summary>US dollars per AI credit, from <c>Arena:Cost:UsdPerAiCredit</c>.</summary>
    public double UsdPerAiCredit => catalog.Value.Cost.UsdPerAiCredit;

    /// <summary>The per-model token averages over the stored moves, for models with at least one such move.</summary>
    public async Task<IReadOnlyDictionary<string, ModelTokenAverage>> HistoryAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await MoveTokenHistory.ReadAsync(db, ct);
    }

    public async Task<IReadOnlyDictionary<string, ModelCostInputs>> ForAsync(ModelListing listing, CancellationToken ct)
    {
        var history = await HistoryAsync(ct);
        return listing.Models.ToDictionary(model => model.Id,
            model => new ModelCostInputs(model.PremiumMultiplier, model.Pricing, history.GetValueOrDefault(model.Id)),
            StringComparer.Ordinal);
    }
}
