using System.Globalization;

namespace Chess.Web.Components.Charts;

/// <summary>The coefficients a model map can put on either axis (03 §13).</summary>
public enum MapCoefficient
{
    Rating,
    ArenaScore,
    Legality,
    MoveQuality,
    Latency,
    Tokens,
    Cost,
    ForfeitRate,
}

/// <summary>
/// One model with every coefficient the map can show, in the units of its axis: legality, move quality and forfeit rate
/// are fractions, latency is seconds per move, tokens are input plus output tokens per move, cost is USD per game. A null
/// value means the model has no such coefficient (no Stockfish data, no priced replies), so the model is left off a map
/// whose axes need it. The bounds are the 95 % intervals.
/// </summary>
public sealed record MapModel(
    string Model,
    int Games,
    bool Provisional,
    int? Slot,
    double? Rating,
    double? RatingLow,
    double? RatingHigh,
    double? ArenaScore,
    double? ArenaScoreLow,
    double? ArenaScoreHigh,
    double? Legality,
    double? MoveQuality,
    double? LatencySeconds,
    double? Tokens,
    double? CostPerGameUsd,
    double? ForfeitRate)
{
    public double? Value(MapCoefficient coefficient) => coefficient switch
    {
        MapCoefficient.Rating => Rating,
        MapCoefficient.ArenaScore => ArenaScore,
        MapCoefficient.Legality => Legality,
        MapCoefficient.MoveQuality => MoveQuality,
        MapCoefficient.Latency => LatencySeconds,
        MapCoefficient.Tokens => Tokens,
        MapCoefficient.Cost => CostPerGameUsd,
        MapCoefficient.ForfeitRate => ForfeitRate,
        _ => throw new ArgumentOutOfRangeException(nameof(coefficient), coefficient, null),
    };

    /// <summary>The 95 % interval of a coefficient that has one (rating and arena score); null otherwise.</summary>
    public (double Low, double High)? Interval(MapCoefficient coefficient) => coefficient switch
    {
        MapCoefficient.Rating when RatingLow is { } low && RatingHigh is { } high => (low, high),
        MapCoefficient.ArenaScore when ArenaScoreLow is { } low && ArenaScoreHigh is { } high => (low, high),
        _ => null,
    };
}

/// <summary>
/// How one coefficient is drawn: its name, its number format, which direction is better, and the words the quadrant
/// names use for its two ends (03 §13: "strong and cheap", "weak but expensive").
/// </summary>
public sealed record MapAxis(
    MapCoefficient Coefficient,
    string Name,
    string Format,
    string Prefix,
    string Suffix,
    bool HigherIsBetter,
    string Good,
    string Bad,
    bool MayBeLog)
{
    /// <summary>The text of a value on this axis: ticks, the table and the hover all use it.</summary>
    public string Text(double value) => Prefix + value.ToString(Format, CultureInfo.InvariantCulture) + Suffix;
}

/// <summary>The axis definitions of the model map, in the order the pickers list them.</summary>
public static class MapAxes
{
    private static readonly MapAxis[] All =
    [
        new(MapCoefficient.Rating, "Rating", "0", "", "", HigherIsBetter: true, Good: "strong", Bad: "weak", MayBeLog: false),
        new(MapCoefficient.ArenaScore, "Arena score", "0.0", "", "", HigherIsBetter: true, Good: "leading", Bad: "trailing", MayBeLog: false),
        new(MapCoefficient.Legality, "First-attempt legality", "0%", "", "", HigherIsBetter: true, Good: "reliable", Bad: "error-prone", MayBeLog: false),
        new(MapCoefficient.MoveQuality, "Move quality", "0.00", "", "", HigherIsBetter: true, Good: "accurate", Bad: "inaccurate", MayBeLog: false),
        new(MapCoefficient.Latency, "Latency per move (s)", "G3", "", "", HigherIsBetter: false, Good: "fast", Bad: "slow", MayBeLog: true),
        new(MapCoefficient.Tokens, "Tokens per move", "#,0", "", "", HigherIsBetter: false, Good: "lean", Bad: "verbose", MayBeLog: true),
        new(MapCoefficient.Cost, "Estimated cost per game (USD)", "G3", "$", "", HigherIsBetter: false, Good: "cheap", Bad: "expensive", MayBeLog: true),
        new(MapCoefficient.ForfeitRate, "Forfeit-loss rate", "0%", "", "", HigherIsBetter: false, Good: "clean", Bad: "forfeit-prone", MayBeLog: false),
    ];

    /// <summary>The axis of a coefficient.</summary>
    public static MapAxis Of(MapCoefficient coefficient) => All.Single(axis => axis.Coefficient == coefficient);

    /// <summary>Every coefficient in picker order. Move quality is left out when Stockfish has not measured the moves.</summary>
    public static IReadOnlyList<MapCoefficient> Choices(bool moveQualityAvailable) =>
        All.Where(axis => axis.Coefficient != MapCoefficient.MoveQuality || moveQualityAvailable)
            .Select(axis => axis.Coefficient)
            .ToList();
}
