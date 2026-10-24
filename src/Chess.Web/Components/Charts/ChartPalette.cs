namespace Chess.Web.Components.Charts;

/// <summary>
/// Colour roles of the charts. Each value is a CSS custom property defined once in
/// <c>ChartFrame.razor.css</c>, so light and dark values live in one place (03 §6.1).
/// Series colours follow the entity, never its rank.
/// </summary>
public static class ChartPalette
{
    /// <summary>The categorical palette has eight fixed slots; a ninth series folds into "Other".</summary>
    public const int SeriesSlots = 8;

    public const string Neutral = "var(--chart-neutral)";
    public const string Accent = "var(--chart-accent)";
    public const string Ink = "var(--chart-ink)";
    public const string SecondaryInk = "var(--chart-ink-2)";
    public const string DivergeLow = "var(--chart-diverge-low)";
    public const string DivergeHigh = "var(--chart-diverge-high)";
    public const string DivergeMid = "var(--chart-diverge-mid)";

    /// <summary>CSS colour of categorical slot <paramref name="slot"/> (0–7).</summary>
    public static string Series(int slot) =>
        slot is >= 0 and < SeriesSlots
            ? $"var(--chart-series-{slot})"
            : throw new ArgumentOutOfRangeException(nameof(slot), slot, $"Categorical slots run 0–{SeriesSlots - 1}.");

    /// <summary>
    /// Assigns slots by the alphabetical position of the keys, so a filter never repaints the survivors.
    /// Only the first eight keys receive a slot; the rest are absent and the caller folds them into "Other".
    /// </summary>
    public static IReadOnlyDictionary<string, int> AssignSlots(IEnumerable<string> keys) =>
        keys.Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Take(SeriesSlots)
            .Select((key, slot) => (Key: key, Slot: slot))
            .ToDictionary(pair => pair.Key, pair => pair.Slot, StringComparer.Ordinal);
}
