using Chess.Api.Catalog;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tournaments;

/// <summary>
/// Gathers the inputs of the cost estimates for the listed models: the catalog's prices and premium multipliers, and the
/// token averages over the stored moves. Tournaments and the broadcast planner both estimate through this.
/// </summary>
public sealed class CostInputs(IDbContextFactory<ArenaDbContext> factory, IOptions<CostEstimateOptions> options)
{
    public CostEstimateOptions Options => options.Value;

    public async Task<IReadOnlyDictionary<string, ModelCostInputs>> ForAsync(ModelListing listing, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var history = await MoveTokenHistory.ReadAsync(db, ct);
        return listing.Models.ToDictionary(model => model.Id,
            model => new ModelCostInputs(model.PremiumMultiplier, model.Pricing, history.GetValueOrDefault(model.Id)),
            StringComparer.Ordinal);
    }
}
