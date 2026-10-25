using Chess.Api.Contracts;
using Chess.Api.Usage;
using Chess.Llm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Chess.Api.Catalog;

/// <summary>
/// The players a game can be started with: the gateway's models, cached, plus the synthetic
/// <c>random</c> player. When the gateway is not ready only <c>random</c> is listed, with a warning.
/// </summary>
public sealed class ModelCatalog(ILlmGateway gateway, IMemoryCache cache, IOptions<CatalogOptions> options)
{
    public const string RandomModelId = "random";

    private const string AutoModelId = "auto";
    private const string CacheKey = "catalog:models";
    private static readonly ModelDto Random = new(RandomModelId, "Random", 0, true);

    public async Task<ModelListing> GetModelsAsync(CancellationToken ct)
    {
        var state = gateway.Status.State;
        if (state is not (LlmGatewayState.Ready or LlmGatewayState.Fake))
        {
            return NotReady(state);
        }

        try
        {
            var models = await cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = options.Value.ModelCacheTtl;
                var listed = await gateway.ListModelsAsync(ct);
                var rate = options.Value.Cost.UsdPerAiCredit;
                return listed
                    .Select(m => new ModelDto(m.Id, m.DisplayName, m.PremiumMultiplier, m.Enabled,
                        ModelPricing.ToDto(m.Pricing, rate)))
                    .ToArray();
            });

            var offered = (models ?? []).Where(m => options.Value.ShowAutoModel || !IsAuto(m));
            return new ModelListing([.. offered, Random], Warning: null);
        }
        catch (LlmUnavailableException)
        {
            // Not cached: the next request asks the gateway again.
            return NotReady(gateway.Status.State);
        }
    }

    private static bool IsAuto(ModelDto model) => model.Id.Equals(AutoModelId, StringComparison.OrdinalIgnoreCase);

    private static ModelListing NotReady(LlmGatewayState state) =>
        new([Random], $"LLM gateway is {state}; only the random player is available");
}

public sealed record ModelListing(IReadOnlyList<ModelDto> Models, string? Warning);
