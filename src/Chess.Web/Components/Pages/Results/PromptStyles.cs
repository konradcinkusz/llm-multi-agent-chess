using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Results;

/// <summary>
/// The prompt styles of the Results page, read from the player ids (03 §14.1, §14.5). Pure: no I/O, no clock.
/// </summary>
public static class PromptStyles
{
    /// <summary>The known styles in the order 03 §14.2 lists them. Any other style sorts after these, by name.</summary>
    private static readonly string[] KnownOrder = [PlayerId.DefaultStyle, "board", "think"];

    public static string StyleOf(string player) => PlayerId.Parse(player).Style;

    /// <summary>The styles the players use, classic first.</summary>
    public static List<string> Present(IEnumerable<string> players) =>
        players
            .Select(StyleOf)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(style => Array.IndexOf(KnownOrder, style) is >= 0 and var index ? index : KnownOrder.Length)
            .ThenBy(style => style, StringComparer.Ordinal)
            .ToList();

    /// <summary>True when <paramref name="style"/> is "All" (null) or the player uses it.</summary>
    public static bool Includes(string player, string? style) => style is null || StyleOf(player) == style;
}
