using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Players.Prompts;
using Chess.Api.Tournaments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>One side of a quote: its resolved player id (with the style it names), its expected USD, or why it cannot be quoted.</summary>
public sealed record SidePrice(string? PlayerId, double? Usd, string? Error);

/// <summary>A quote for a matchup. <see cref="Quote"/> is null when a side cannot be quoted; <see cref="BudgetUsd"/> is the game's net budget.</summary>
public sealed record QuoteOutcome(QuoteDto? Quote, double? BudgetUsd, string? Error);

/// <summary>
/// The request menu and the quotes made from it (03 §15.2). Prices come from the model catalog, the stored move history and the
/// prompt style's assumption. A menu entry that lists no prices is left out of the menu, and a quote for it is refused.
/// </summary>
public sealed class RequestMenu(IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog,
    IOptions<RequestOptions> requests, IOptions<CatalogOptions> catalogOptions)
{
    private const string Unpriced = "This model lists no prices, so it cannot be quoted.";

    public async Task<RequestMenuDto> MenuAsync(CancellationToken ct)
    {
        var options = requests.Value;
        var listing = await catalog.GetModelsAsync(ct);
        var history = await ReadHistoryAsync(ct);
        var rate = catalogOptions.Value.Cost.UsdPerAiCredit;

        var items = new List<MenuItemDto>();
        foreach (var entry in options.Menu)
        {
            // An entry without a price is not offered: a viewer could not be quoted for it.
            var side = PriceSide(entry.Code, listing, history, options, rate);
            if (side.Usd is { } usd)
            {
                items.Add(new MenuItemDto(entry.Code, entry.PlayerId, entry.Label ?? entry.PlayerId, usd));
            }
        }

        return new RequestMenuDto(items, PromptStyles.All.Select(style => style.Id).ToArray(),
            options.Tiers.Order().ToArray(), options.FeeShare, RequestCommandParser.Usage);
    }

    public async Task<QuoteOutcome> QuoteAsync(string white, string black, CancellationToken ct)
    {
        var options = requests.Value;
        var listing = await catalog.GetModelsAsync(ct);
        var history = await ReadHistoryAsync(ct);
        var rate = catalogOptions.Value.Cost.UsdPerAiCredit;

        var whiteSide = PriceSide(white, listing, history, options, rate);
        var blackSide = PriceSide(black, listing, history, options, rate);
        var error = whiteSide.Error ?? blackSide.Error;
        if (error is not null)
        {
            return new QuoteOutcome(null, null, error);
        }

        var price = RequestPricing.PriceUsd(whiteSide.Usd, blackSide.Usd, options);
        var quote = new QuoteDto(whiteSide.PlayerId!, blackSide.PlayerId!, whiteSide.Usd!.Value + blackSide.Usd!.Value, price);
        return new QuoteOutcome(quote, price * (1 - options.FeeShare), null);
    }

    /// <summary>
    /// Prices one side. <paramref name="side"/> is a menu code, optionally with <c>~style</c>; the default style is classic.
    /// Pure given its inputs: the catalog listing, the stored history and the options.
    /// </summary>
    public static SidePrice PriceSide(string side, ModelListing listing, IReadOnlyDictionary<string, ModelTokenAverage> history,
        RequestOptions options, double usdPerAiCredit)
    {
        var (code, styleOrNull) = RequestCommandParser.SplitSide(side);
        if (code is null)
        {
            return new SidePrice(null, null, "A side needs a menu code.");
        }

        var entry = options.Menu.FirstOrDefault(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new SidePrice(null, null, "Not a menu code.");
        }

        var style = styleOrNull ?? PromptStyles.DefaultId;
        if (PromptStyles.Find(style) is null)
        {
            return new SidePrice(null, null, "Not a prompt style.");
        }

        var playerId = style == PromptStyles.DefaultId ? entry.PlayerId : $"{entry.PlayerId}~{style}";
        if (ModelVariant.Problem(entry.PlayerId, listing) is { } problem)
        {
            return new SidePrice(null, null, problem);
        }

        var model = ModelVariant.BaseOf(entry.PlayerId);
        var listed = listing.Models.FirstOrDefault(m => m.Id == model);
        if (listed is null)
        {
            return new SidePrice(null, null, $"{model} is not offered by the gateway right now.");
        }

        // The player's own history first, then its model's, then the style's assumption (03 §15.2).
        var playerHistory = history.GetValueOrDefault(entry.PlayerId) ?? history.GetValueOrDefault(model);
        var usd = RequestPricing.SideEstimateUsd(model, listed.Pricing, playerHistory, options.Styles[style], usdPerAiCredit);
        return usd is null
            ? new SidePrice(playerId, null, Unpriced)
            : new SidePrice(playerId, usd, null);
    }

    private async Task<IReadOnlyDictionary<string, ModelTokenAverage>> ReadHistoryAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await MoveTokenHistory.ReadAsync(db, ct);
    }
}
