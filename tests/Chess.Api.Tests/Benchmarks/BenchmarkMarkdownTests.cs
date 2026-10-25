using Chess.Api.Benchmarks;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

public class BenchmarkMarkdownTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheMarkdownHasTheSummaryTheOpponentTableAndTheFingerprint()
    {
        var run = new BenchmarkDto(Guid.Empty, "Medium | pick", "fake-a@high", ["fake-b"], 4, "finished", 4, 4, At, At);
        var report = new BenchmarkReportDto(run,
            [new BenchmarkOpponentDto("fake-b", 4, 3, 0, 1, 0, 0.75, 190.85, 12.0, null, 0.61, 0.4, 0.8, 0.9, 0.0123)],
            new BenchmarkSummaryDto(4, 0.75, null, null, null, null, true, 4200, 0.0123, 0.0456),
            new BenchmarkFingerprintDto("1.0.0+abc1234", "1", "sha256:0123456789ab", At));

        var markdown = BenchmarkMarkdown.Render(report);

        Assert.Contains("# Benchmark: Medium \\| pick", markdown);
        Assert.Contains("| Opponent | Games | W | D | L |", markdown);
        Assert.Contains("| fake-b | 4 | 3 | 0 | 1 | 0 | 75.0 % | +191 (+12 to ∞) | 0.61 (0.40 to 0.80) | 90.0 % | $0.0123 |", markdown);
        Assert.Contains("App version: 1.0.0+abc1234", markdown);
        Assert.Contains("Rating formula version (RATING.md): 1", markdown);
        Assert.Contains("Prompt version: sha256:0123456789ab", markdown);
    }

    [Fact]
    public void AMissingRatingIsWrittenAsADashNotAZero()
    {
        var run = new BenchmarkDto(Guid.Empty, "Pick", "fake-a", [], 2, "running", 0, 0, At, null);
        var report = new BenchmarkReportDto(run, [],
            new BenchmarkSummaryDto(0, 0, null, null, null, null, true, 0, null, null),
            new BenchmarkFingerprintDto("unknown", "1", "sha256:x", At));

        var markdown = BenchmarkMarkdown.Render(report);

        Assert.Contains("| Rating (95 % interval) | — |", markdown);
        Assert.Contains("| Candidate cost | — |", markdown);
    }
}
