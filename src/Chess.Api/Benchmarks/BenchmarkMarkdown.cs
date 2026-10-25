using System.Globalization;
using System.Text;
using Chess.Api.Contracts;

namespace Chess.Api.Benchmarks;

/// <summary>
/// The Markdown report a benchmark is shared as (03 §12.2, <c>?format=md</c>): the summary, one row per opponent, and the
/// fingerprint. Missing numbers are written as an em dash; an unbounded Elo bound as ∞.
/// </summary>
public static class BenchmarkMarkdown
{
    private const string Missing = "—";
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Render(BenchmarkReportDto report)
    {
        var benchmark = report.Benchmark;
        var summary = report.Summary;
        var fingerprint = report.Fingerprint;
        var text = new StringBuilder();

        text.AppendLine($"# Benchmark: {Cell(benchmark.Name)}").AppendLine();
        text.AppendLine($"Candidate `{benchmark.Candidate}` against {benchmark.Opponents.Count} opponent(s), " +
            $"{benchmark.GamesPerOpponent} games each. Status: {benchmark.Status}. " +
            $"{summary.Games} of {benchmark.GamesTotal} games decided.").AppendLine();

        text.AppendLine("## Summary").AppendLine();
        text.AppendLine("| Measure | Value |").AppendLine("|---|---|");
        text.AppendLine($"| Score | {Percent(summary.Score)} |");
        text.AppendLine($"| Rating (95 % interval) | {Interval(summary.Rating, summary.RatingLow, summary.RatingHigh, "0")} |");
        text.AppendLine($"| Arena score | {Number(summary.ArenaScore, "0.00")} |");
        text.AppendLine($"| Provisional | {(summary.Provisional ? "yes" : "no")} |");
        text.AppendLine($"| Candidate tokens | {summary.CandidateTokens.ToString("N0", Culture)} |");
        text.AppendLine($"| Candidate cost | {Usd(summary.CandidateEstimatedUsd)} |");
        text.AppendLine($"| Total cost | {Usd(summary.TotalEstimatedUsd)} |").AppendLine();

        text.AppendLine("## Against each opponent").AppendLine();
        text.AppendLine("| Opponent | Games | W | D | L | Forfeits | Score | Elo difference (95 % interval) " +
            "| RQC (95 % interval) | First-try legal | Candidate cost |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|");
        foreach (var opponent in report.Opponents)
        {
            text.AppendLine($"| {Cell(opponent.Opponent)} | {opponent.Games} | {opponent.Wins} | {opponent.Draws} | " +
                $"{opponent.Losses} | {opponent.ForfeitLosses} | {Percent(opponent.Score)} | " +
                $"{Interval(opponent.EloDiff, opponent.EloDiffLow, opponent.EloDiffHigh, "+0;-0;0")} | " +
                $"{Interval(opponent.Rqc, opponent.RqcLow, opponent.RqcHigh, "0.00")} | " +
                $"{Percent(opponent.CandidateFirstAttemptLegalRate)} | {Usd(opponent.CandidateEstimatedUsd)} |");
        }

        text.AppendLine().AppendLine("## Fingerprint").AppendLine();
        text.AppendLine($"- App version: {Cell(fingerprint.AppVersion)}");
        text.AppendLine($"- Rating formula version (RATING.md): {Cell(fingerprint.FormulaVersion)}");
        text.AppendLine($"- Prompt version: {Cell(fingerprint.PromptVersion)}");
        text.AppendLine($"- Generated: {fingerprint.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", Culture)}");
        return text.ToString();
    }

    private static string Percent(double fraction) => (fraction * 100).ToString("0.0", Culture) + " %";

    private static string Number(double? value, string format) =>
        value is { } number ? number.ToString(format, Culture) : Missing;

    /// <summary>A point with its bounds, as <c>point (low to high)</c>. An unbounded bound is ∞ or −∞.</summary>
    private static string Interval(double? point, double? low, double? high, string format)
    {
        if (point is null)
        {
            return Missing;
        }

        var lowText = low is { } l ? l.ToString(format, Culture) : "−∞";
        var highText = high is { } h ? h.ToString(format, Culture) : "∞";
        return $"{point.Value.ToString(format, Culture)} ({lowText} to {highText})";
    }

    private static string Usd(double? value) => value is { } usd ? "$" + usd.ToString("0.0000", Culture) : Missing;

    // A pipe would end a table cell early, and a line break would end the row.
    private static string Cell(string text) => text.Replace("|", "\\|").ReplaceLineEndings(" ");
}
