using Chess.Api.Contracts;
using Chess.Api.Usage;
using Chess.Api.Players.Prompts;
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
/// A player id is <c>model[@effort][~style]</c> (03 §12.1, §14.1). The effort is part of the player's identity (games, ratings,
/// results), so each configuration is ranked as its own player. A plain model id means the model's default effort, and a plain
/// id means the classic prompt style. A model id may not contain <c>@</c> or <c>~</c>. Pure: no I/O.
/// </summary>
public static class ModelVariant
{
    private const char EffortSeparator = '@';
    private const char StyleSeparator = '~';
    private static readonly char[] Delimiters = [EffortSeparator, StyleSeparator];

    /// <summary>The model, effort and prompt style of a player id. The effort is null for a plain model id.</summary>
    public sealed record Parts(string Model, string? Effort, string Style);

    /// <summary>The parts of a player id; the style is <see cref="PromptStyles.DefaultId"/> when the id names none.</summary>
    /// <exception cref="ArgumentException">The id is not <c>model[@effort][~style]</c>.</exception>
    public static Parts Parse(string playerId)
    {
        if (!TryParts(playerId, out var model, out var effort, out var style))
        {
            throw new ArgumentException(MalformedMessage(playerId), nameof(playerId));
        }

        return new Parts(model, effort, style ?? PromptStyles.DefaultId);
    }

    /// <summary>The model and effort of a player id; the effort is null for a plain model id. The style is read by <see cref="Parse"/>.</summary>
    /// <exception cref="ArgumentException">The id is not <c>model[@effort][~style]</c>.</exception>
    public static (string Model, string? Effort) Split(string playerId)
    {
        var parts = Parse(playerId);
        return (parts.Model, parts.Effort);
    }

    /// <summary>The model of a player id, without its effort and style.</summary>
    public static string BaseOf(string playerId)
    {
        var cut = playerId.IndexOfAny(Delimiters);
        return cut < 0 ? playerId : playerId[..cut];
    }

    /// <summary>The style named by a player id, or <see cref="PromptStyles.DefaultId"/> for a plain id. It is not checked against the styles.</summary>
    public static string StyleOf(string playerId)
    {
        var tilde = playerId.IndexOf(StyleSeparator);
        return tilde < 0 ? PromptStyles.DefaultId : playerId[(tilde + 1)..];
    }

    /// <returns>What is wrong with a player id, or null when it is fine. Only an id that names an effort is checked against the catalog.</returns>
    public static string? Problem(string playerId, ModelListing listing)
    {
        if (!TryParts(playerId, out var model, out var effort, out var style))
        {
            return MalformedMessage(playerId);
        }

        if (style is not null)
        {
            if (style == PromptStyles.DefaultId)
            {
                return $"'{playerId}' names the default style: write the plain id, {model}{(effort is null ? string.Empty : "@" + effort)}.";
            }

            if (PromptStyles.Find(style) is null)
            {
                return $"'{style}' is not a prompt style. The styles are: {string.Join(", ", PromptStyles.All.Select(s => s.Id))}.";
            }

            if (model == ModelCatalog.RandomModelId)
            {
                return "The random player takes no prompt style.";
            }
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

    private static string MalformedMessage(string playerId) =>
        $"'{playerId}' is not a player id: write it as model[@effort][~style], with no more than one @ and one ~, and the @ before the ~.";

    /// <summary>
    /// Splits <c>model[@effort][~style]</c>. The style is cut first, so an <c>@</c> after the <c>~</c> is malformed. Every part must
    /// be non-empty, and neither the style nor the effort may hold a delimiter.
    /// </summary>
    private static bool TryParts(string playerId, out string model, out string? effort, out string? style)
    {
        model = string.Empty;
        effort = null;
        style = null;

        var body = playerId;
        var tilde = playerId.IndexOf(StyleSeparator);
        if (tilde >= 0)
        {
            body = playerId[..tilde];
            style = playerId[(tilde + 1)..];
            if (style.Length == 0 || style.IndexOfAny(Delimiters) >= 0)
            {
                return false;
            }
        }

        var at = body.IndexOf(EffortSeparator);
        if (at < 0)
        {
            model = body;
            return model.Length > 0;
        }

        model = body[..at];
        effort = body[(at + 1)..];
        return model.Length > 0 && effort.Length > 0 && effort.IndexOf(EffortSeparator) < 0;
    }
}
