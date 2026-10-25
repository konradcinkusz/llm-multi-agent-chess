using System.Globalization;
using Chess.Web.Api;

namespace Chess.Web.Components.Shared.Game;

/// <summary>
/// Tokens, AI credits and dollars as the live page, the replay and the summary show them. Pure, so the wording is tested
/// without a page. Credits and dollars are always estimates (03 §10.2): the labels say so.
/// </summary>
public static class GameFigures
{
    /// <summary>The default <c>Arena:Cost:UsdPerAiCredit</c>, used until a game's usage (which carries the rate) is loaded.</summary>
    public const double DefaultUsdPerAiCredit = 0.01;

    public static string Tokens(long? count) => count is { } value ? value.ToString("N0", CultureInfo.InvariantCulture) : "—";

    public static string Credits(double? credits) =>
        credits is { } value ? $"{value.ToString("0.0000", CultureInfo.InvariantCulture)} cr" : "—";

    /// <summary>Dollars with two decimals, or four below one cent so a small estimate does not read as $0.00.</summary>
    public static string Usd(double? usd)
    {
        if (usd is not { } value)
        {
            return "—";
        }

        return value < 0.01
            ? $"${value.ToString("0.0000", CultureInfo.InvariantCulture)}"
            : $"${value.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    public static string Rate(double usdPerAiCredit) =>
        $"${usdPerAiCredit.ToString("0.0000", CultureInfo.InvariantCulture)} per AI credit";

    public static string Latency(long milliseconds) =>
        $"{(milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} s";

    /// <summary>
    /// A model's price per 1M tokens, as the pickers show it: <c>$3.00 / $15.00 per 1M tokens</c>. Null when the model has
    /// no price at all, so the caller can say "price unknown" instead.
    /// </summary>
    public static string? Price(ModelPricingDto? pricing)
    {
        if (pricing is null || (pricing.InputUsdPerMillionTokens is null && pricing.OutputUsdPerMillionTokens is null))
        {
            return null;
        }

        return $"{PerMillion(pricing.InputUsdPerMillionTokens)} / {PerMillion(pricing.OutputUsdPerMillionTokens)} per 1M tokens";
    }

    /// <summary>What a cost basis (03 §10.2) means, in words.</summary>
    public static string Basis(string costBasis) => costBasis switch
    {
        "free" => "free (random player)",
        "reported" => "reported by Copilot",
        "estimated" => "estimated from token prices",
        "partial" => "partly estimated (some replies unknown)",
        "unknown" => "unknown",
        _ => costBasis,
    };

    private static string PerMillion(double? usd) =>
        usd is { } value ? $"${value.ToString("0.00", CultureInfo.InvariantCulture)}" : "—";
}
