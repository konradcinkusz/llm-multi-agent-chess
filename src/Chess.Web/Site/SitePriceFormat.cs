using System.Globalization;

namespace Chess.Web.Site;

/// <summary>How the menu page writes prices. Pure, so the page and its tests agree.</summary>
public static class SitePriceFormat
{
    /// <summary>A dollar amount as "$1.50".</summary>
    public static string Usd(double amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A fraction as a whole percentage, "30 %".</summary>
    public static string Percent(double fraction) => Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " %";
}
