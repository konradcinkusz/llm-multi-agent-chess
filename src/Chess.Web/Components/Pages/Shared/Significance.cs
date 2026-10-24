using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Shared;

/// <summary>
/// The one rule the UI applies before it says a result is "significant" (RATING.md §5, "When the UI may say
/// 'significantly'"). Pure: no I/O, no clock.
/// </summary>
public static class Significance
{
    /// <summary>RQC of one half means equal strength.</summary>
    public const double Half = 0.5;

    /// <summary>Below this an interval is a point, not a range: every bootstrap resample agreed on the same number.</summary>
    public const double Tolerance = 1e-6;

    /// <summary>Rated games a model needs before a lead against it may be called significant (RATING.md §6).</summary>
    public const int MinimumRatedGames = 20;

    /// <summary>The note shown when a result is not significant only because a model has too few games.</summary>
    public static readonly string TooFewGames = $"Not yet significant: too few games (need {MinimumRatedGames} rated games per model).";

    /// <summary>
    /// True when the 95 % interval excludes one half by a real margin and is not a point. Says nothing about sample
    /// size: see <see cref="IsSignificant"/> for the full rule.
    /// </summary>
    public static bool IntervalShowsLead(double low, double high) =>
        high - low > Tolerance && (low > Half + Tolerance || high < Half - Tolerance);

    /// <summary>
    /// A pairwise result is significant only when the interval shows a lead (<see cref="IntervalShowsLead"/>) and
    /// both models are established (not provisional, i.e. at least <see cref="MinimumRatedGames"/> rated games).
    /// </summary>
    public static bool IsSignificant(PairwiseCoefficient pair, bool bothEstablished) =>
        bothEstablished && IntervalShowsLead(pair.RqcLow, pair.RqcHigh);

    /// <summary>True when both named models appear in the ratings and neither is provisional.</summary>
    public static bool BothEstablished(IEnumerable<ModelRating> ratings, string a, string b)
    {
        var byModel = ratings.ToDictionary(r => r.Model, r => r.Provisional, StringComparer.Ordinal);
        return byModel.TryGetValue(a, out var aProvisional) && !aProvisional
            && byModel.TryGetValue(b, out var bProvisional) && !bProvisional;
    }
}
