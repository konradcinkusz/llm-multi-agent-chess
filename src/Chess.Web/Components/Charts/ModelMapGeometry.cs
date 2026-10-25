using System.Globalization;
using static Chess.Web.Components.Charts.ChartGeometry;

namespace Chess.Web.Components.Charts;

/// <summary>One bubble: its centre and radius in the viewBox, its whiskers, its label and its hover text.</summary>
public sealed record MapMark(
    string Model,
    double X,
    double Y,
    double Radius,
    int? Slot,
    bool Provisional,
    (double From, double To)? WhiskerX,
    (double From, double To)? WhiskerY,
    double LabelX,
    double LabelY,
    string LabelAnchor,
    string Tooltip);

/// <summary>A tick of an axis: its position in the viewBox and its text.</summary>
public sealed record MapTick(double Position, string Text);

/// <summary>A quadrant name in one corner of the plot.</summary>
public sealed record MapQuadrant(string Name, double X, double Y, string Anchor);

/// <summary>Everything the model map draws, computed without rendering (03 §13).</summary>
public sealed record ModelMapView(
    IReadOnlyList<MapMark> Marks,
    IReadOnlyList<MapTick> XTicks,
    IReadOnlyList<MapTick> YTicks,
    double? MedianX,
    double? MedianY,
    IReadOnlyList<MapQuadrant> Quadrants,
    string XTitle,
    string YTitle,
    string Reading,
    IReadOnlyList<string> Unplaced,
    IReadOnlyList<string> TableHeaders,
    IReadOnlyList<IReadOnlyList<string>> TableRows);

/// <summary>
/// The geometry of the model map. Pure: the caller passes the models and the two chosen coefficients, and gets the marks,
/// ticks, guides and names to draw. The rules are 03 §13: a log scale when every value is positive and the range exceeds
/// 10×; bubble area proportional to the rated games; quadrant names from the better direction of each axis.
/// </summary>
public static class ModelMapGeometry
{
    /// <summary>The viewBox of the map, in pixels.</summary>
    public const double Width = 720;
    public const double Height = 420;

    /// <summary>The plot area inside the viewBox. The margins hold the ticks and the axis titles.</summary>
    public const double PlotLeft = 64;
    public const double PlotRight = 696;
    public const double PlotTop = 24;
    public const double PlotBottom = 364;

    private const double MinRadius = 5;
    private const double MaxRadius = 22;
    private const double LabelGap = 14;
    private const double LabelInset = 6;
    private const double CornerInset = 8;

    /// <summary>A log scale is used only when the range of the values exceeds this factor.</summary>
    private const double LogRange = 10;

    public static ModelMapView Build(IReadOnlyList<MapModel> models, MapCoefficient xCoefficient, MapCoefficient yCoefficient)
    {
        var x = MapAxes.Of(xCoefficient);
        var y = MapAxes.Of(yCoefficient);
        var placed = models.Where(m => m.Value(xCoefficient) is not null && m.Value(yCoefficient) is not null).ToList();
        var unplaced = models
            .Where(m => !placed.Contains(m))
            .Select(m => $"{m.Model}: no {MissingNames(m, x, y)}")
            .ToList();
        var table = Table(models);

        if (placed.Count == 0)
        {
            return new ModelMapView([], [], [], null, null, [], AxisTitle(x, false), AxisTitle(y, false),
                Reading(x, y), unplaced, table.Headers, table.Rows);
        }

        var xScale = ScaleOf(x, placed, xCoefficient, PlotLeft, PlotRight);
        var yScale = ScaleOf(y, placed, yCoefficient, PlotBottom, PlotTop);
        var maxGames = placed.Max(m => m.Games);

        var marks = placed.Select(m => new
        {
            Model = m,
            X = xScale.Map(m.Value(xCoefficient)!.Value),
            Y = yScale.Map(m.Value(yCoefficient)!.Value),
        }).ToList();
        var labelYs = SeparateLabels(marks.Select(m => m.Y).ToArray(), LabelGap);

        var positioned = marks.Select((mark, i) =>
        {
            var radius = maxGames == 0 ? MinRadius : Math.Max(MinRadius, MaxRadius * Math.Sqrt((double)mark.Model.Games / maxGames));
            var labelY = Math.Clamp(labelYs[i], PlotTop + 10, PlotBottom - 4);
            var startX = mark.X + radius + LabelInset;
            var fits = startX + mark.Model.Model.Length * LabelCharWidth <= PlotRight;
            return new MapMark(
                mark.Model.Model,
                mark.X,
                mark.Y,
                radius,
                mark.Model.Slot,
                mark.Model.Provisional,
                Whisker(mark.Model, xCoefficient, xScale),
                Whisker(mark.Model, yCoefficient, yScale),
                fits ? startX : mark.X - radius - LabelInset,
                labelY,
                fits ? "start" : "end",
                Tooltip(mark.Model));
        }).ToList();

        return new ModelMapView(
            positioned,
            Ticks(xScale, x),
            Ticks(yScale, y),
            xScale.Map(Median(placed.Select(m => m.Value(xCoefficient)!.Value))),
            yScale.Map(Median(placed.Select(m => m.Value(yCoefficient)!.Value))),
            Quadrants(x, y),
            AxisTitle(x, xScale.Log),
            AxisTitle(y, yScale.Log),
            Reading(x, y),
            unplaced,
            table.Headers,
            table.Rows);
    }

    /// <summary>
    /// The scale of one axis over the placed models, including their interval ends so that whiskers fit. A log scale when
    /// every value is positive and the range exceeds 10×; a degenerate axis (all values equal) gets one tick.
    /// </summary>
    private static MapScale ScaleOf(MapAxis axis, IReadOnlyList<MapModel> placed, MapCoefficient coefficient, double rangeLow, double rangeHigh)
    {
        var values = placed.SelectMany(m => Ends(m, coefficient)).ToList();
        var min = values.Min();
        var max = values.Max();
        if (axis.MayBeLog && min > 0 && max / min > LogRange)
        {
            var low = Math.Pow(10, Math.Floor(Math.Log10(min)));
            var high = Math.Pow(10, Math.Ceiling(Math.Log10(max)));
            return new MapScale(true, new LinearScale(Math.Log10(low), Math.Log10(high), rangeLow, rangeHigh), LogTicks(low, high));
        }

        // Only values that are exactly equal are degenerate. ChartGeometry.PointAxis would also flatten values that
        // differ below two decimals, which is every cost of a cheap model.
        var (domainLow, domainHigh, ticks) = max > min ? NiceAxis(min, max) : DegenerateAxis(min);
        return new MapScale(false, new LinearScale(domainLow, domainHigh, rangeLow, rangeHigh), ticks);
    }

    private static (double Low, double High, IReadOnlyList<double> Ticks) DegenerateAxis(double value) => (value - 1, value + 1, [value]);

    /// <summary>1, 2 and 5 times each power of ten between the bounds, the bounds included.</summary>
    private static IReadOnlyList<double> LogTicks(double low, double high)
    {
        var ticks = new List<double>();
        for (var power = Math.Floor(Math.Log10(low)); power <= Math.Ceiling(Math.Log10(high)); power++)
        {
            foreach (var step in new[] { 1d, 2, 5 })
            {
                var value = step * Math.Pow(10, power);
                if (value >= low * (1 - 1e-9) && value <= high * (1 + 1e-9))
                {
                    ticks.Add(value);
                }
            }
        }

        return ticks;
    }

    private static IEnumerable<double> Ends(MapModel model, MapCoefficient coefficient)
    {
        yield return model.Value(coefficient)!.Value;
        if (model.Interval(coefficient) is { } interval)
        {
            yield return interval.Low;
            yield return interval.High;
        }
    }

    private static (double From, double To)? Whisker(MapModel model, MapCoefficient coefficient, MapScale scale) =>
        model.Interval(coefficient) is { } interval ? (scale.Map(interval.Low), scale.Map(interval.High)) : null;

    /// <summary>
    /// The ticks of an axis. A tick whose text repeats the one before it is dropped: at three significant figures two
    /// close ticks can read the same, and a row of identical labels says nothing (07 §6).
    /// </summary>
    private static IReadOnlyList<MapTick> Ticks(MapScale scale, MapAxis axis)
    {
        var ticks = new List<MapTick>();
        foreach (var value in scale.Ticks)
        {
            var text = axis.Text(value);
            if (ticks.Count == 0 || ticks[^1].Text != text)
            {
                ticks.Add(new MapTick(scale.Map(value), text));
            }
        }

        return ticks;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>The four quadrants, each named by the better direction of its two ends (03 §13).</summary>
    private static IReadOnlyList<MapQuadrant> Quadrants(MapAxis x, MapAxis y)
    {
        var quadrants = new List<MapQuadrant>();
        foreach (var atRight in new[] { false, true })
        {
            foreach (var atTop in new[] { false, true })
            {
                // The end of an axis is the better one when it is the end that axis rewards.
                var xGood = atRight == x.HigherIsBetter;
                var yGood = atTop == y.HigherIsBetter;
                quadrants.Add(new MapQuadrant(
                    QuadrantName(x, y, xGood, yGood),
                    atRight ? PlotRight - CornerInset : PlotLeft + CornerInset,
                    atTop ? PlotTop + 16 : PlotBottom - CornerInset,
                    atRight ? "end" : "start"));
            }
        }

        return quadrants;
    }

    private static string QuadrantName(MapAxis x, MapAxis y, bool xGood, bool yGood) => (xGood, yGood) switch
    {
        (true, true) => $"{y.Good} and {x.Good}",
        (false, true) => $"{y.Good} but {x.Bad}",
        (true, false) => $"{y.Bad} but {x.Good}",
        _ => $"{y.Bad} and {x.Bad}",
    };

    private static string Reading(MapAxis x, MapAxis y) =>
        $"{(y.HigherIsBetter ? "Up" : "Down")} is better for {Lower(y.Name)}; {(x.HigherIsBetter ? "right" : "left")} is better for {Lower(x.Name)}.";

    /// <summary>A name inside a sentence: its first letter lower-cased, the rest as written (USD stays upper case).</summary>
    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string AxisTitle(MapAxis axis, bool log) => log ? $"{axis.Name}, log scale" : axis.Name;

    private static string MissingNames(MapModel model, MapAxis x, MapAxis y)
    {
        var missing = new List<string>();
        if (model.Value(x.Coefficient) is null)
        {
            missing.Add(Lower(x.Name));
        }

        if (model.Value(y.Coefficient) is null)
        {
            missing.Add(Lower(y.Name));
        }

        return string.Join(" and ", missing);
    }

    /// <summary>The hover text of a bubble: every coefficient of the model, and how many games it rests on.</summary>
    private static string Tooltip(MapModel model) =>
        $"{model.Model}: {string.Join(", ", Coefficients(model).Select(c => $"{Lower(c.Name)} {c.Text}"))}, " +
        $"{model.Games} rated {(model.Games == 1 ? "game" : "games")}";

    private static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) Table(IReadOnlyList<MapModel> models)
    {
        var headers = new List<string> { "Model" };
        headers.AddRange(MapAxes.Choices(moveQualityAvailable: true).Select(c => MapAxes.Of(c).Name));
        headers.Add("Rated games");

        var rows = models.Select(model => (IReadOnlyList<string>)
            new List<string> { model.Model }
                .Concat(MapAxes.Choices(moveQualityAvailable: true).Select(c => CellText(model, c)))
                .Append(model.Games.ToString(CultureInfo.InvariantCulture))
                .ToList()).ToList();
        return (headers, rows);
    }

    private static string CellText(MapModel model, MapCoefficient coefficient)
    {
        var axis = MapAxes.Of(coefficient);
        if (model.Value(coefficient) is not { } value)
        {
            return "—";
        }

        var text = axis.Text(value);
        return model.Interval(coefficient) is { } interval
            ? $"{text} ({axis.Text(interval.Low)} to {axis.Text(interval.High)})"
            : text;
    }

    private static IEnumerable<(string Name, string Text)> Coefficients(MapModel model) =>
        MapAxes.Choices(moveQualityAvailable: true)
            .Select(c => (MapAxes.Of(c).Name, CellText(model, c)));

    /// <summary>
    /// One axis as the scale of the plot: a linear or log map from a value to a pixel position, and the values it ticks at.
    /// </summary>
    private sealed record MapScale(bool Log, LinearScale Inner, IReadOnlyList<double> Ticks)
    {
        public double Map(double value) => Inner.Map(Log ? Math.Log10(value) : value);
    }
}
