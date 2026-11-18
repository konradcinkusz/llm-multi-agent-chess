using System.Globalization;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Shared;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>Text and timing shared by the tournament pages.</summary>
public static class TournamentFormat
{
    /// <summary>How often the tournament pages reload while a tournament can still change.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    public static bool IsRunning(string status) => status == "running";

    public static string Status(string status) => status switch
    {
        "running" => "Running",
        "finished" => "Finished",
        "cancelled" => "Cancelled",
        _ => status,
    };

    /// <summary>The badge variant (07 §4) for a status: a running tournament is accented, a finished one is a success.</summary>
    public static string StatusBadge(string status) => status switch
    {
        "running" => "badge-accent",
        "finished" => "badge-success",
        _ => "badge-neutral",
    };

    /// <summary>
    /// A premium-request figure; "by token" when the model has no multiplier. Copilot bills those models in AI credits
    /// per token (03 §10), so their cost is in the credit and USD columns, not unknown.
    /// </summary>
    public static string Requests(double? value) =>
        value is { } requests ? requests.ToString("#,##0.#", CultureInfo.InvariantCulture) : "by token";

    /// <summary>What an estimate table says of the random player's price and cost: it plays no model, so it costs nothing.</summary>
    public const string Free = "free";

    /// <summary>True for the random player, whatever its id carries (it has no effort or style, but the check does not rely on it).</summary>
    public static bool IsFree(string playerId) => PlayerId.Parse(playerId).Model == RandomModelId;

    public static string Price(string playerId, ModelPricingDto? pricing) => IsFree(playerId) ? Free : Price(pricing);

    public static string Requests(string playerId, double? value) => IsFree(playerId) ? Free : Requests(value);

    public static string Credits(string playerId, double? value) => IsFree(playerId) ? Free : Credits(value);

    public static string Usd(string playerId, double? value) => IsFree(playerId) ? Free : Usd(value);

    /// <summary>A model's token prices as the forms show them (03 §10): "$3.00 / $15.00 per 1M tokens", or "price unknown".</summary>
    public static string Price(ModelPricingDto? pricing) =>
        pricing is { InputUsdPerMillionTokens: { } input, OutputUsdPerMillionTokens: { } output }
            ? $"{Money(input)} / {Money(output)} per 1M tokens"
            : "price unknown";

    /// <summary>
    /// A model's checkbox label: its token price when the API lists one, else its premium multiplier, else "price unknown".
    /// The random player is free.
    /// </summary>
    public static string Cost(ModelDto model)
    {
        if (model.Id == RandomModelId)
        {
            return "free";
        }

        if (model.Pricing is { InputUsdPerMillionTokens: not null, OutputUsdPerMillionTokens: not null })
        {
            return Price(model.Pricing);
        }

        return model.PremiumMultiplier is { } multiplier
            ? "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture) + " premium requests"
            : "price unknown";
    }

    /// <summary>An AI-credit figure: "24.5 credits", or "unknown" when the prices are not known.</summary>
    public static string Credits(double? value) =>
        value is { } credits ? credits.ToString("#,##0.#", CultureInfo.InvariantCulture) + " credits" : "unknown";

    /// <summary>A USD estimate: "$0.24", or "unknown". Sub-cent amounts keep four decimals so they do not read as $0.00.</summary>
    public static string Usd(double? value) => value is { } usd ? Money(usd) : "unknown";

    /// <summary>Where a model's tokens per move came from, in words: "from 12 past moves", "assumed tokens per move", ...</summary>
    public static string Basis(string? basis, int? sampleMoves) => basis switch
    {
        "history" => $"from {(sampleMoves ?? 0).ToString("#,##0", CultureInfo.InvariantCulture)} past moves",
        "assumed" => "assumed tokens per move",
        "free" => "free",
        "unknown" => "price unknown",
        _ => "",
    };

    public const string RandomModelId = "random";

    private static string Money(double value) =>
        value is > 0 and < 0.01
            ? value.ToString("$0.0000", CultureInfo.InvariantCulture)
            : value.ToString("$0.00", CultureInfo.InvariantCulture);

    public static string Multiplier(double? value) =>
        value is { } multiplier ? "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture) : "multiplier unknown";

    public static string Fixed(double value, string format) => NumberText.Fixed(value, format);

    public static string Percent(double value) => NumberText.Fixed(value, "P0");

    public static string When(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
