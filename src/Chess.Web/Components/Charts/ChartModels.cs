namespace Chess.Web.Components.Charts;

/// <summary>A horizontal bar with an optional interval (whisker). Provisional bars are hatched and labelled.</summary>
public sealed record RankedBar(string Label, double Value, double? Low = null, double? High = null,
    bool Highlighted = false, bool Provisional = false);

/// <summary>One category of a single-series bar chart.</summary>
public sealed record CategoryValue(string Label, double Value);

/// <summary>A point on a numeric x axis.</summary>
public sealed record ChartPoint(double X, double Y);

/// <summary>One line of a line chart. <paramref name="Slot"/> comes from <see cref="ChartPalette.AssignSlots"/>.</summary>
public sealed record LineSeries(string Name, int Slot, IReadOnlyList<ChartPoint> Points);

/// <summary>A labelled point of a scatter plot.</summary>
public sealed record ScatterPoint(string Label, double X, double Y);

/// <summary>One cell of a heatmap. A null <paramref name="Value"/> leaves the cell empty.</summary>
public sealed record HeatCell(string Row, string Column, double? Value, bool Muted = false);

/// <summary>One segment of a 100 % stacked bar. <paramref name="Slot"/> is the categorical slot.</summary>
public sealed record StackSegment(string Name, int Slot, double Value);

/// <summary>One row of a 100 % stacked bar chart.</summary>
public sealed record StackRow(string Label, IReadOnlyList<StackSegment> Segments);

/// <summary>One legend entry: the series name and the CSS colour of its mark.</summary>
public sealed record ChartLegendItem(string Name, string Color);
