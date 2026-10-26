using Chess.Api.Players.Prompts;

namespace Chess.Api.Requests;

/// <summary>
/// A parsed <c>!g</c> command: two menu codes, each with the prompt style it names (or null for the default). The codes are
/// as typed, not yet looked up in the menu.
/// </summary>
public sealed record RequestCommand(string WhiteCode, string? WhiteStyle, string BlackCode, string? BlackStyle);

/// <summary>The outcome of parsing a message that starts with <c>!g</c>. Exactly one of the two is set.</summary>
/// <param name="Command">The command, when the message parsed.</param>
/// <param name="Error">What is wrong with it. It names no text the viewer typed, since rejections are shown on the stream.</param>
public sealed record CommandParse(RequestCommand? Command, string? Error);

/// <summary>
/// Reads <c>!g &lt;white&gt; vs &lt;black&gt; [style]</c> and <c>!g &lt;white&gt; &lt;black&gt; [style]</c> (03 §15.3). Each side is a
/// menu code with an optional <c>~style</c>. A trailing style applies to any side that names none. Matching is
/// case-insensitive, and extra words are ignored. Pure: no menu, no I/O.
/// </summary>
public static class RequestCommandParser
{
    public const string Trigger = "!g";
    private const string Versus = "vs";
    private const char StyleSeparator = '~';

    /// <summary>The usage shown when a command is missing a side.</summary>
    public const string Usage = "!g <white> vs <black> [style], for example !g c1 vs d3 think";

    /// <returns>True when the message is a command, that is it starts with <c>!g</c> as a word of its own.</returns>
    public static bool IsCommand(string text) => Words(text) is [var first, ..] && first.Equals(Trigger, StringComparison.OrdinalIgnoreCase);

    /// <returns>Null when the message is not a command; otherwise the parsed command or the reason it is malformed.</returns>
    public static CommandParse? Parse(string text)
    {
        if (!IsCommand(text))
        {
            return null;
        }

        var sides = new List<string>(2);
        string? sharedStyle = null;

        foreach (var word in Words(text).Skip(1))
        {
            if (sides.Count < 2)
            {
                if (!word.Equals(Versus, StringComparison.OrdinalIgnoreCase))
                {
                    sides.Add(word);
                }
            }
            else if (sharedStyle is null && PromptStyles.Find(word.ToLowerInvariant()) is not null)
            {
                sharedStyle = word.ToLowerInvariant();
            }
        }

        if (sides.Count < 2)
        {
            return new CommandParse(null, $"Two menu codes are needed: {Usage}.");
        }

        var (white, whiteStyle) = SplitSide(sides[0]);
        var (black, blackStyle) = SplitSide(sides[1]);
        if (white is null || black is null)
        {
            return new CommandParse(null, $"A side needs a menu code: {Usage}.");
        }

        var styles = new[] { whiteStyle, blackStyle };
        if (styles.Any(style => style is not null && PromptStyles.Find(style) is null))
        {
            return new CommandParse(null, $"The prompt style is not one of: {string.Join(", ", PromptStyles.All.Select(s => s.Id))}.");
        }

        return new CommandParse(new RequestCommand(white, whiteStyle ?? sharedStyle, black, blackStyle ?? sharedStyle), null);
    }

    /// <summary>
    /// Splits a side (<c>code</c> or <c>code~style</c>) into its code and style, both lower-cased. The code is null when it is
    /// empty; the style is null when the side names none, and empty when the <c>~</c> has nothing after it.
    /// </summary>
    public static (string? Code, string? Style) SplitSide(string side)
    {
        var cut = side.IndexOf(StyleSeparator);
        var code = (cut < 0 ? side : side[..cut]).ToLowerInvariant();
        var style = cut < 0 ? null : side[(cut + 1)..].ToLowerInvariant();

        return (code.Length == 0 ? null : code, style);
    }

    private static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
