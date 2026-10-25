namespace Chess.Web.Api;

/// <summary>
/// The parts of a player id, <c>model[@effort][~style]</c> (03 §12.1, §14.1). The API owns the rules (<c>ModelVariant</c>
/// in Chess.Api); the Web project may not reference it (P3), so this is a copy that builds and reads ids the same way.
/// A missing style is <see cref="DefaultStyle"/>. Pure: no I/O.
/// </summary>
public readonly record struct PlayerId(string Model, string? Effort, string Style)
{
    public const char EffortSeparator = '@';
    public const char StyleSeparator = '~';
    public const string DefaultStyle = "classic";

    /// <summary>The parts of <paramref name="id"/>. Text the API would reject is read as far as it goes, never thrown on.</summary>
    public static PlayerId Parse(string id)
    {
        var tilde = id.IndexOf(StyleSeparator);
        var style = tilde < 0 ? DefaultStyle : id[(tilde + 1)..];
        var rest = tilde < 0 ? id : id[..tilde];
        var at = rest.IndexOf(EffortSeparator);
        return at < 0
            ? new PlayerId(rest, null, style)
            : new PlayerId(rest[..at], rest[(at + 1)..], style);
    }

    /// <summary>The id of a model, effort and style. The default style is left out, so a classic player keeps its plain id.</summary>
    public static string Compose(string model, string? effort, string? style)
    {
        var id = string.IsNullOrEmpty(effort) ? model : model + EffortSeparator + effort;
        return string.IsNullOrEmpty(style) || style == DefaultStyle ? id : id + StyleSeparator + style;
    }

    /// <summary>The id without its style: what "Prompt styles compared" groups by.</summary>
    public string WithoutStyle => Compose(Model, Effort, null);

    public override string ToString() => Compose(Model, Effort, Style);
}
