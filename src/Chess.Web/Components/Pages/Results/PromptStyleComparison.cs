using Chess.Web.Api;
using Chess.Web.Components.Pages.Shared;

namespace Chess.Web.Components.Pages.Results;

/// <summary>One style of a comparison group: the player (a model@effort under one style) and its verdict.</summary>
/// <param name="Against">The verdict against the baseline style: "Baseline", or the head-to-head reading of the one rule.</param>
public sealed record StyleRow(string Player, string Style, ModelRating Rating, double? CostPerGame, string Against);

/// <summary>The styles of one model@effort that played under two or more styles (03 §14.5).</summary>
public sealed record StyleGroup(string Configuration, IReadOnlyList<StyleRow> Rows);

/// <summary>
/// "Prompt styles compared": every model@effort played under two or more styles, one row per style. Each style is compared
/// with the baseline style (classic when it played, else the first style in <see cref="PromptStyles.Present"/>) through the
/// head-to-head matrix, and the significance follows <see cref="Significance"/> only. Pure: no I/O, no clock.
/// </summary>
public static class PromptStyleComparison
{
    public static IReadOnlyList<StyleGroup> Build(
        IReadOnlyList<ModelRating> ranking,
        IReadOnlyList<PairwiseCoefficient> cells,
        IReadOnlyDictionary<string, ModelResultsDto> breakdown)
    {
        return ranking
            .GroupBy(rating => PlayerId.Parse(rating.Model).WithoutStyle, StringComparer.Ordinal)
            .Where(group => group.Select(rating => PromptStyles.StyleOf(rating.Model)).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group =>
            {
                var styles = PromptStyles.Present(group.Select(rating => rating.Model));
                var baseline = group.First(rating => PromptStyles.StyleOf(rating.Model) == styles[0]).Model;
                var rows = group
                    .OrderBy(rating => styles.IndexOf(PromptStyles.StyleOf(rating.Model)))
                    .Select(rating => new StyleRow(
                        rating.Model,
                        PromptStyles.StyleOf(rating.Model),
                        rating,
                        breakdown.TryGetValue(rating.Model, out var results) ? results.AvgEstimatedUsdPerGame : null,
                        rating.Model == baseline
                            ? "Baseline"
                            : AgainstBaseline(ranking, cells, rating.Model, baseline, styles[0])))
                    .ToList();
                return new StyleGroup(group.Key, rows);
            })
            .ToList();
    }

    /// <summary>
    /// The head-to-head reading of one style against the baseline. A lead is called significant only by
    /// <see cref="Significance.IsSignificant"/>; a lead that is not significant only because a model is provisional says so.
    /// </summary>
    private static string AgainstBaseline(IReadOnlyList<ModelRating> ranking, IReadOnlyList<PairwiseCoefficient> cells,
        string player, string baseline, string baselineStyle)
    {
        var pair = cells.FirstOrDefault(cell => cell.A == player && cell.B == baseline);
        if (pair is null) return "No head-to-head games yet.";

        var established = Significance.BothEstablished(ranking, player, baseline);
        var significant = Significance.IsSignificant(pair, established);
        var reading = significant
            ? pair.Rqc >= Significance.Half ? $"Significantly ahead of {baselineStyle}" : $"Significantly behind {baselineStyle}"
            : $"No significant difference from {baselineStyle}";
        var sentence = $"{reading}: RQC {ResultsModel.Number(pair.Rqc, "0.00")}, " +
                       $"95 % interval {ResultsModel.Number(pair.RqcLow, "0.00")} to {ResultsModel.Number(pair.RqcHigh, "0.00")}.";
        return !established && Significance.IntervalShowsLead(pair.RqcLow, pair.RqcHigh)
            ? $"{sentence} {Significance.TooFewGames}"
            : sentence;
    }
}
