using System.Globalization;

namespace Chess.Web.Components.Charts;

/// <summary>Maps a data domain linearly onto a pixel range. The range may run backwards (a y axis).</summary>
internal readonly record struct LinearScale(double DomainMin, double DomainMax, double RangeMin, double RangeMax)
{
    public double Map(double value) =>
        DomainMax == DomainMin
            ? RangeMin
            : RangeMin + (value - DomainMin) / (DomainMax - DomainMin) * (RangeMax - RangeMin);
}

/// <summary>Pure geometry shared by the chart components: axes, bar outlines and label spacing.</summary>
internal static class ChartGeometry
{
    /// <summary>Gap between adjacent fills in pixels (03 §6.1: 2 px).</summary>
    public const double Gap = 2;

    /// <summary>Radius of the rounded data end in pixels (03 §6.1: 4 px).</summary>
    public const double CornerRadius = 4;

    public static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Widens [min, max] to whole nice steps (1, 2 or 5 × 10^n) and returns that axis's ticks.</summary>
    public static (double Min, double Max, IReadOnlyList<double> Ticks) NiceAxis(double min, double max, int count = 5)
    {
        if (max <= min) max = min + 1;
        var step = NiceStep((max - min) / count);
        var first = (int)Math.Floor(min / step);
        var last = (int)Math.Ceiling(max / step);
        var ticks = Enumerable.Range(first, last - first + 1).Select(k => Math.Round(k * step, 10)).ToList();
        return (ticks[0], ticks[^1], ticks);
    }

    private static double NiceStep(double raw)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalised = raw / magnitude;
        var nice = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>
    /// Outline of a horizontal bar starting at the baseline <paramref name="x"/>. The data end (right) is
    /// rounded when <paramref name="roundEnd"/> is set; the baseline end stays square.
    /// </summary>
    public static string HorizontalBar(double x, double y, double width, double height, bool roundEnd)
    {
        if (width <= 0 || height <= 0) return "";
        var right = x + width;
        var bottom = y + height;
        var r = roundEnd ? Math.Min(CornerRadius, Math.Min(width, height / 2)) : 0;
        if (r == 0) return $"M{Num(x)},{Num(y)}H{Num(right)}V{Num(bottom)}H{Num(x)}Z";
        return $"M{Num(x)},{Num(y)}H{Num(right - r)}Q{Num(right)},{Num(y)} {Num(right)},{Num(y + r)}" +
               $"V{Num(bottom - r)}Q{Num(right)},{Num(bottom)} {Num(right - r)},{Num(bottom)}H{Num(x)}Z";
    }

    /// <summary>Outline of a vertical bar standing on its baseline; the top (data end) is rounded.</summary>
    public static string VerticalBar(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return "";
        var right = x + width;
        var bottom = y + height;
        var r = Math.Min(CornerRadius, Math.Min(width / 2, height));
        return $"M{Num(x)},{Num(bottom)}V{Num(y + r)}Q{Num(x)},{Num(y)} {Num(x + r)},{Num(y)}" +
               $"H{Num(right - r)}Q{Num(right)},{Num(y)} {Num(right)},{Num(y + r)}V{Num(bottom)}Z";
    }

    /// <summary>Pushes labels apart so that none sits closer than <paramref name="minGap"/> to the one above it. Input order is kept.</summary>
    public static double[] SeparateLabels(IReadOnlyList<double> positions, double minGap)
    {
        var result = positions.ToArray();
        var order = Enumerable.Range(0, result.Length).OrderBy(i => result[i]).ToArray();
        for (var k = 1; k < order.Length; k++)
        {
            var previous = order[k - 1];
            var current = order[k];
            if (result[current] - result[previous] < minGap)
                result[current] = result[previous] + minGap;
        }
        return result;
    }
}
