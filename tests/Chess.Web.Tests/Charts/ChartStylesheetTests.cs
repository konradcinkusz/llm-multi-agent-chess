using System.Text.RegularExpressions;
using Xunit;

namespace Chess.Web.Tests.Charts;

/// <summary>Pins the colour rules of 03 §6.1 against the stylesheet and the component sources.</summary>
public class ChartStylesheetTests
{
    // 03 §6.1: categorical palette, fixed order.
    private static readonly string[] LightSeries =
        ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];

    private static readonly string[] DarkSeries =
        ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];

    [Fact]
    public void SeriesTokensHoldTheLightThenDarkPaletteOfTheSpec()
    {
        var css = File.ReadAllText(Path.Combine(ChartSources.ChartsPath, "ChartFrame.razor.css"));
        var darkStart = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal);
        Assert.True(darkStart > 0, "the dark values must sit under prefers-color-scheme: dark");

        var declarations = Regex.Matches(css, @"--chart-series-(\d):\s*(#[0-9a-fA-F]{6});")
            .Select(m => (Index: int.Parse(m.Groups[1].Value), Value: m.Groups[2].Value.ToLowerInvariant(), Offset: m.Index))
            .ToList();

        var light = declarations.Where(d => d.Offset < darkStart).OrderBy(d => d.Index).Select(d => d.Value).ToArray();
        var dark = declarations.Where(d => d.Offset > darkStart).OrderBy(d => d.Index).Select(d => d.Value).ToArray();
        Assert.Equal(LightSeries, light);
        Assert.Equal(DarkSeries, dark);
    }

    [Fact]
    public void ChartComponentsNeverNameAColourLiteral()
    {
        var sources = Directory.EnumerateFiles(ChartSources.ChartsPath)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal));

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"#[0-9a-fA-F]{6}\b"), $"{Path.GetFileName(file)} contains a hex colour; use a token.");
        }
    }
}
