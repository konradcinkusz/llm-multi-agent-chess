using System.Text.RegularExpressions;
using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>
/// Pins the stylesheet against 07-UI-STYLE: the tokens of §2 in both themes, the shared classes of §4 and the colour
/// rules of §1. The test reads <c>wwwroot/app.css</c> from disk, the same way the chart tests read their sources.
/// </summary>
public class StyleContractTests
{
    // 07 §2, colour tokens: defined in the light block and redefined in the dark block.
    private static readonly string[] ColourTokens =
    [
        "--bg", "--surface", "--surface-2", "--border", "--border-strong", "--text", "--text-2", "--text-3",
        "--accent", "--accent-hover", "--accent-contrast", "--accent-soft", "--success", "--success-soft",
        "--warning", "--warning-soft", "--danger", "--danger-soft", "--shadow-1",
    ];

    // 07 §2, shape, spacing and type: the same in both themes, so defined once in the light block.
    private static readonly string[] ShapeAndTypeTokens =
    [
        "--radius-sm", "--radius", "--radius-lg",
        "--space-1", "--space-2", "--space-3", "--space-4", "--space-5", "--space-6", "--space-7",
        "--gutter", "--content-max", "--font-sans", "--font-mono",
        "--text-xs", "--text-sm", "--text-base", "--text-lg", "--text-xl", "--text-2xl", "--text-3xl",
    ];

    // 07 §4, plus the progress fill variant used for a finished run (progress-bar-success).
    private static readonly string[] SharedClasses =
    [
        "page-header", "page-lede", "page-actions", "card", "card-header", "card-title", "stack", "stack-lg",
        "cluster", "grid-2", "btn", "btn-primary", "btn-danger", "btn-sm", "field", "field-hint", "field-row",
        "chips", "chip", "table-wrap", "table-compact", "num", "badge", "badge-success", "badge-warning",
        "badge-danger", "badge-accent", "badge-neutral", "dot", "dot-success", "dot-warning", "dot-danger",
        "dot-neutral", "dot-accent", "callout", "callout-info", "callout-warning", "callout-danger", "empty-state",
        "muted", "error-text", "status-error", "progress", "progress-bar", "progress-bar-success",
    ];

    // Named colours that a declaration value could use instead of a token (system colours included).
    private static readonly Regex NamedColour = new(
        @"(?<![\w-])(black|white|red|green|blue|gray|grey|silver|maroon|navy|teal|olive|lime|aqua|fuchsia|purple|yellow|orange|canvas|canvastext)(?![\w-])",
        RegexOptions.IgnoreCase);

    private static readonly Regex LightBlock = new(@":root\s*\{([^}]*)\}");
    private static readonly Regex DarkBlock = new(@"@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}\s*\}");

    private static readonly string Css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Chess.Web", "wwwroot", "app.css"));

    private static readonly string LightDeclarations = LightBlock.Match(Css).Groups[1].Value;
    private static readonly string DarkDeclarations = DarkBlock.Match(Css).Groups[1].Value;

    [Fact]
    public void EveryColourTokenIsDefinedInTheLightBlock()
    {
        var missing = ColourTokens.Where(t => !Holds(LightDeclarations, t)).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryColourTokenIsRedefinedInTheDarkBlock()
    {
        var missing = ColourTokens.Where(t => !Holds(DarkDeclarations, t)).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryShapeAndTypeTokenIsDefinedInTheLightBlock()
    {
        var missing = ShapeAndTypeTokens.Where(t => !Holds(LightDeclarations, t)).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void TheDarkBlockRedefinesColourTokensAndNothingElse()
    {
        var declared = Regex.Matches(DarkDeclarations, @"(--[\w-]+)\s*:").Select(m => m.Groups[1].Value);

        var extra = declared.Except(ColourTokens).ToArray();
        Assert.Empty(extra);
    }

    [Fact]
    public void EverySharedClassHasARule()
    {
        var css = Uncommented(Css);

        var unruled = SharedClasses.Where(c => !Regex.IsMatch(css, $@"\.{Regex.Escape(c)}(?![\w-])")).ToArray();
        Assert.Empty(unruled);
    }

    [Fact]
    public void ColourComesOnlyFromTheTokenBlocks()
    {
        var outside = Uncommented(Css)
            .Replace(DarkBlock.Match(Css).Value, string.Empty)
            .Replace(LightBlock.Match(Css).Value, string.Empty);

        Assert.False(Regex.IsMatch(outside, @"#[0-9a-fA-F]{3,8}\b"), "app.css names a hex colour outside the token blocks.");
        Assert.False(Regex.IsMatch(outside, @"\b(rgb|rgba|hsl|hsla)\(", RegexOptions.IgnoreCase), "app.css names a functional colour outside the token blocks.");

        var values = Regex.Matches(outside, @":\s*([^;{}]+)").Select(m => m.Groups[1].Value);
        var named = values.Where(v => NamedColour.IsMatch(v)).ToArray();
        Assert.Empty(named);
    }

    [Fact]
    public void FocusAndReducedMotionAreStyled()
    {
        Assert.Contains(":focus-visible", Css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", Css, StringComparison.Ordinal);
    }

    private static bool Holds(string declarations, string token) =>
        Regex.IsMatch(declarations, $@"(?<![\w-]){Regex.Escape(token)}\s*:");

    private static string Uncommented(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Chess.sln was not found above the test binaries.");
    }
}
