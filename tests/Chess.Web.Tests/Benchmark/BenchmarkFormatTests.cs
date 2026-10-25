using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkFormatTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AnEloDifferenceIsShownWithItsSignedIntervalAsAReaderReadsIt()
    {
        Assert.Equal("+191 (+120 to +260)", BenchmarkFormat.Elo(191, 120, 260, 0.75));
        Assert.Equal("−40 (−60 to −10)", BenchmarkFormat.Elo(-40.4, -60, -10, 0.4));
    }

    [Fact]
    public void AnUnboundedEloShowsInfinityOnTheSideThatIsUnbounded()
    {
        // A sweep: the point is unbounded, its lower bound is finite and its upper bound is unbounded.
        Assert.Equal("+∞ (+120 to +∞)", BenchmarkFormat.Elo(null, 120, null, 1));

        // No win at all, and both bounds unbounded.
        Assert.Equal("−∞ (−∞ to +∞)", BenchmarkFormat.Elo(null, null, null, 0));
    }

    [Fact]
    public void AnEvenScoreWithNoBoundsIsZeroPlusInfinitySpan()
    {
        Assert.Equal("+0 (−∞ to +∞)", BenchmarkFormat.Elo(0, null, null, 0.5));
    }

    [Fact]
    public void TheGlobalRatingIsRoundedAndItsIntervalIsWordedOrADash()
    {
        Assert.Equal("1500", BenchmarkFormat.Rating(1500));
        Assert.Equal("—", BenchmarkFormat.Rating(null));
        Assert.Equal("95 % interval +1400 to +1600", BenchmarkFormat.Interval(1400, 1600));
        Assert.Equal("95 % interval −125 to +206", BenchmarkFormat.Interval(-125.2, 205.6));
        Assert.Equal("", BenchmarkFormat.Interval(null, 1600));
    }

    [Fact]
    public void ScoresAndLegalRatesAreShownAsPercentages()
    {
        Assert.Equal("62.5 %", BenchmarkFormat.Percent(0.625));
        Assert.Equal("75 %", BenchmarkFormat.Share(0.75));
        Assert.Equal("—", BenchmarkFormat.Fixed(null, "0.00"));
        Assert.Equal("0.50", BenchmarkFormat.Fixed(0.5, "0.00"));
    }

    [Theory]
    [InlineData("Spring check!", "md", "benchmark-spring-check.md")]
    [InlineData("A  B--C", "json", "benchmark-a-b-c.json")]
    [InlineData("  ??  ", "json", "benchmark-report.json")]
    public void TheDownloadNameIsASlugOfTheBenchmarkName(string name, string extension, string expected)
    {
        Assert.Equal(expected, BenchmarkFormat.FileName(name, extension));
    }

    [Fact]
    public void ADataLinkCarriesTheTextItself()
    {
        const string markdown = "# Report\n\n50 % # of 100 %\n";

        var href = BenchmarkFormat.DataUrl(BenchmarkFormat.MarkdownMediaType, markdown);

        Assert.StartsWith("data:text/markdown;charset=utf-8,", href, StringComparison.Ordinal);
        Assert.Equal(markdown, Uri.UnescapeDataString(href["data:text/markdown;charset=utf-8,".Length..]));
    }

    [Fact]
    public void TheJsonDownloadIsTheReportAsTheApiShapesIt()
    {
        var report = JsonSerializer.Deserialize<BenchmarkReportDto>(Fixtures.Report, Options)!;

        var json = BenchmarkFormat.Json(report);
        var back = JsonSerializer.Deserialize<BenchmarkReportDto>(json, Options)!;

        Assert.Contains("\"formulaVersion\": \"RATING-1\"", json);
        Assert.Equal(report.Summary, back.Summary);
        Assert.Equal(report.Opponents.Count, back.Opponents.Count);
        Assert.Null(back.Opponents[0].EloDiffHigh);
    }
}
