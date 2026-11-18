using System.Text.RegularExpressions;
using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>
/// Pins the accessibility findings of the live axe survey against the sources, so they do not come back: a scrollable
/// table region is focusable and named, and the visually hidden utility has one definition.
/// </summary>
public class AccessibilityContractTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string WebRoot = Path.Combine(Root, "src", "Chess.Web");

    // The broadcast planner page is owned by another stream; it is left out until it carries the same attributes.
    private static readonly string[] NotCovered = ["PlanPage.razor"];

    [Fact]
    public void EveryTableWrapIsAKeyboardFocusableNamedRegion()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot, "Components"), "*.razor", SearchOption.AllDirectories))
        {
            if (NotCovered.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            foreach (Match tag in Regex.Matches(File.ReadAllText(file), @"<div class=""[^""]*\btable-wrap\b[^""]*""[^>]*>"))
            {
                var text = tag.Value;
                if (!text.Contains("tabindex=\"0\"", StringComparison.Ordinal)
                    || !text.Contains("role=\"region\"", StringComparison.Ordinal)
                    || !Regex.IsMatch(text, @"aria-label(ledby)?=""[^""]+"""))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {text}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void ATableRegionIsNotNamedLikeTheHeadingOfItsSection()
    {
        // axe "landmark-unique": a section labelled by its heading and a scroll region inside it with the same text are
        // two landmarks of the same name (found in the live survey of the merged streams).
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot, "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var headings = Regex.Matches(source, @"<h[1-6][^>]*>([^<@]+)</h[1-6]>").Select(m => m.Groups[1].Value.Trim()).ToHashSet();
            foreach (Match tag in Regex.Matches(source, @"<div class=""[^""]*\btable-wrap\b[^""]*""[^>]*aria-label=""([^""]+)"""))
            {
                if (headings.Contains(tag.Groups[1].Value))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {tag.Groups[1].Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheModelMapQuadrantNamesAreStyledThroughTheScopeBoundary()
    {
        // ChartText renders the <text> from code, so a scoped rule without ::deep never matches it and the name fell back to
        // black text on the dark background (found in the dark-theme survey of the merged streams).
        var css = File.ReadAllText(Path.Combine(WebRoot, "Components", "Charts", "ModelMap.razor.css"));
        Assert.Contains("::deep .model-map-quadrant", css);
    }

    [Fact]
    public void TheVisuallyHiddenUtilityIsDefinedOnceInTheSharedStylesheet()
    {
        var css = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "app.css"));
        Assert.Single(Regex.Matches(css, @"^\.visually-hidden\s*\{", RegexOptions.Multiline));

        var scoped = Directory.EnumerateFiles(Path.Combine(WebRoot, "Components"), "*.razor.css", SearchOption.AllDirectories)
            .Where(file => !NotCovered.Any(name => file.EndsWith(name + ".css", StringComparison.Ordinal)))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"^\.visually-hidden\s*\{", RegexOptions.Multiline))
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Empty(scoped);
    }

    [Fact]
    public void LinksInRunningTextAreUnderlinedInTheAppShell()
    {
        var css = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "app.css"));

        var rule = Regex.Match(css, @":where\(\.app-shell\) a \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("text-decoration: underline", rule, StringComparison.Ordinal);
        // The pills of the navigation are controls, not running text, so they switch it off.
        var nav = Regex.Match(css, @"\.nav a,\s*\.nav-more > summary \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("text-decoration: none", nav, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Chess.sln was not found above the test binaries.");
    }
}
