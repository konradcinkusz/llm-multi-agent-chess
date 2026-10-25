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

    public const string AutoModelId = "auto";
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
                        ModelPricing.ToDto(m.Pricing, rate), m.ReasoningEfforts, m.DefaultReasoningEffort, m.Custom))
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

    private static bool IsAuto(ModelDto model) => IsAutoModel(model.Id);

    public static bool IsAutoModel(string modelId) => modelId.Equals(AutoModelId, StringComparison.OrdinalIgnoreCase);

    private static ModelListing NotReady(LlmGatewayState state) =>
        new([Random], $"LLM gateway is {state}; only the random player is available");
}

public sealed record ModelListing(IReadOnlyList<ModelDto> Models, string? Warning);

/// <summary>
/// A player id may name a reasoning effort: <c>model@effort</c> (03 §12.1). The effort is part of the player's identity
/// (games, ratings, results), so each configuration is ranked as its own player. A plain model id means the model's default
/// effort. A model id may not contain <c>@</c>. Pure: no I/O.
/// </summary>
public static class ModelVariant
{
    private const char Separator = '@';

    /// <summary>The model and effort of a player id; the effort is null for a plain model id.</summary>
    /// <exception cref="ArgumentException">The id is not <c>model</c> or <c>model@effort</c>.</exception>
    public static (string Model, string? Effort) Split(string playerId)
    {
        if (!TryParts(playerId, out var model, out var effort))
        {
            throw new ArgumentException($"'{playerId}' is not a player id: write a variant as model@effort, with one @.", nameof(playerId));
        }

        return (model, effort);
    }

    /// <summary>The model of a player id, without its effort.</summary>
    public static string BaseOf(string playerId)
    {
        var at = playerId.IndexOf(Separator);
        return at < 0 ? playerId : playerId[..at];
    }

    /// <returns>What is wrong with a player id, or null when it is fine. Only an id that names an effort is checked against the catalog.</returns>
    public static string? Problem(string playerId, ModelListing listing)
    {
        if (!TryParts(playerId, out var model, out var effort))
        {
            return $"'{playerId}' is not a player id: write a variant as model@effort, with one @.";
        }

        if (effort is null)
        {
            return null;
        }

        var listed = listing.Models.FirstOrDefault(m => m.Id == model);
        if (listed is null)
        {
            return $"'{model}' is not a model the gateway offers, so its reasoning effort cannot be checked.";
        }

        if (listed.ReasoningEfforts is not { Count: > 0 } efforts)
        {
            return $"{model} does not take a reasoning effort.";
        }

        return efforts.Contains(effort, StringComparer.Ordinal)
            ? null
            : $"{model} does not offer reasoning effort '{effort}'. It offers: {string.Join(", ", efforts)}.";
    }

    private static bool TryParts(string playerId, out string model, out string? effort)
    {
        var at = playerId.IndexOf(Separator);
        if (at < 0)
        {
            model = playerId;
            effort = null;
            return model.Length > 0;
        }

        model = playerId[..at];
        effort = playerId[(at + 1)..];
        return model.Length > 0 && effort.Length > 0 && effort.IndexOf(Separator) < 0;
    }
}
