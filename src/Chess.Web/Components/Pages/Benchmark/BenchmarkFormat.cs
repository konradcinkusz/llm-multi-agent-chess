using System.Globalization;
using System.Text;
using System.Text.Json;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Benchmark;

/// <summary>Text, names and download links of the benchmark pages (03 §12.2, §12.3).</summary>
public static class BenchmarkFormat
{
    public const string MarkdownMediaType = "text/markdown";
    public const string JsonMediaType = "application/json";

    /// <summary>The API's Elo difference is unbounded, and null, when a score is 0 or 1 or a bound is infinite (03 §12.2).</summary>
    private const string PositiveInfinity = "+∞";
    private const string NegativeInfinity = "−∞";

    /// <summary>
    /// An Elo difference with its 95 % interval: "+191 (−∞ to +260)". A null point is unbounded in the direction of its
    /// score (a 100 % score is +∞); a null bound is unbounded on its side.
    /// </summary>
    public static string Elo(double? diff, double? low, double? high, double score)
    {
        var point = diff is { } value ? Signed(value) : score >= 0.5 ? PositiveInfinity : NegativeInfinity;
        var lower = low is { } l ? Signed(l) : NegativeInfinity;
        var upper = high is { } h ? Signed(h) : PositiveInfinity;
        return $"{point} ({lower} to {upper})";
    }

    /// <summary>The global rating, rounded: "1532"; "—" when the global fit has none.</summary>
    public static string Rating(double? rating) => rating is { } value ? Whole(value) : "—";

    /// <summary>The 95 % interval of a figure, "95 % interval +1400 to +1600"; empty when either bound is missing.</summary>
    public static string Interval(double? low, double? high) =>
        low is { } l && high is { } h ? $"95 % interval {Signed(l)} to {Signed(h)}" : "";

    /// <summary>A score as a percentage with one decimal: "62.5 %". Invariant, so the page reads the same in every culture.</summary>
    public static string Percent(double score) =>
        (score * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    /// <summary>A whole number, rounded: "1532".</summary>
    public static string Whole(double value) =>
        Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>A rate as a whole percentage: "97 %".</summary>
    public static string Share(double value) =>
        (value * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>A figure with a fixed number of decimals, or "—" when the report has none.</summary>
    public static string Fixed(double? value, string format) =>
        value is { } number ? number.ToString(format, CultureInfo.InvariantCulture) : "—";

    /// <summary>An Elo value with its sign, "+191" or "−40" (a true minus sign).</summary>
    public static string Signed(double value) =>
        (value >= 0 ? "+" : "−") + Math.Abs(Math.Round(value)).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The file name of a download: <c>benchmark-&lt;slug&gt;.&lt;ext&gt;</c>, where the slug is the benchmark's name in
    /// lower case with every other run of characters turned into a hyphen.
    /// </summary>
    public static string FileName(string name, string extension)
    {
        var slug = new StringBuilder();
        foreach (var character in name.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var trimmed = slug.ToString().TrimEnd('-');
        return $"benchmark-{(trimmed.Length == 0 ? "report" : trimmed)}.{extension}";
    }

    /// <summary>The report as JSON, the shape <c>GET /api/benchmarks/{id}/report</c> returns, indented for a reader.</summary>
    public static string Json(BenchmarkReportDto report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    /// <summary>A link target that carries the text itself, so the download needs no request from the browser (03 §6).</summary>
    public static string DataUrl(string mediaType, string text) =>
        $"data:{mediaType};charset=utf-8,{Uri.EscapeDataString(text)}";
}
