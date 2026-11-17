using System.Globalization;

namespace Chess.Web.Components.Pages.Shared;

/// <summary>
/// Number formatting for the pages, invariant so every culture reads the same. A figure that rounds to zero is shown
/// without a sign: <c>(-0.3).ToString("0")</c> is "-0", which read as a rating of minus nought on the tournament leaderboard.
/// </summary>
public static class NumberText
{
    /// <summary><paramref name="value"/> in <paramref name="format"/>; a negative that rounds to zero loses its minus sign.</summary>
    public static string Fixed(double value, string format) => WithoutNegativeZero(value.ToString(format, CultureInfo.InvariantCulture));

    /// <summary>"-0", "-0.00" and "-0 %" read as their unsigned selves; every other text is returned as it is.</summary>
    public static string WithoutNegativeZero(string text)
    {
        if (text.Length < 2 || text[0] != '-')
        {
            return text;
        }

        var sawDigit = false;
        foreach (var c in text.AsSpan(1))
        {
            if (c is >= '1' and <= '9')
            {
                return text;
            }

            sawDigit |= c == '0';
        }

        return sawDigit ? text[1..] : text;
    }

    /// <summary>
    /// A figure with its sign, "+191" or "-40", to the decimals of <paramref name="format"/> ("0" or "0.0"). Zero after
    /// rounding has no sign.
    /// </summary>
    public static string Signed(double value, string format = "0")
    {
        var text = Fixed(value, format);
        return text[0] == '-' || IsZero(text) ? text : "+" + text;
    }

    private static bool IsZero(string text) => text.All(c => c is '0' or '.' or ',');
}
