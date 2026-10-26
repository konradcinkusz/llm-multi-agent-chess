using System.Globalization;
using Chess.Web.Api;
using Chess.Web.Components.Charts;
using Chess.Web.Components.Pages.Shared;

namespace Chess.Web.Components.Pages.Results;

/// <summary>What the Results page loads for one scope: all games, or one tournament.</summary>
public sealed record ResultsInputs(
    LeaderboardDto Board,                                       // scoped: the numbers the page shows
    IReadOnlyList<string> AllModels,                            // unscoped: the palette slots never repaint on a filter
    MatrixDto Matrix,                                           // scoped
    IReadOnlyList<ModelResultsDto> Breakdown,                   // scoped
    PairwiseCoefficient? LeaderVsSecond,                        // scoped; null with fewer than two models
    IReadOnlyDictionary<string, IReadOnlyList<RatingHistoryPointDto>> History); // unscoped; keyed by the drawn models

/// <summary>Chart inputs and sentences of the sections of 03 §6.1 (the model map of 03 §13 is the second), in the order the page shows them.</summary>
public sealed record ResultsReport(
    IReadOnlyList<ModelRating> Ranking,
    IReadOnlyList<RankedBar> Bars,
    string? Verdict,
    IReadOnlyList<LineSeries> Lines,
    IReadOnlyList<string> Folded,
    IReadOnlyList<string> HeatLabels,
    IReadOnlyList<HeatCell> Heat,
    IReadOnlyList<StackRow> Outcomes,
    IReadOnlyList<CategoryValue> Legality,
    IReadOnlyList<CategoryValue>? Acpl,
    IReadOnlyList<ScatterPoint> Scatter,
    IReadOnlyList<MapModel> MapModels,
    IReadOnlyList<string> Styles,                              // the prompt styles present in the scope; the filter's chips
    IReadOnlyList<StyleGroup> StyleGroups);                    // "Prompt styles compared" (03 §14.5); empty when none

/// <summary>
/// The two leaderboard orders of the Results page (RATING.md §6). The values are the API's <c>order</c> parameter.
/// </summary>
public static class RankingOrder
{
    public const string Score = "score";
    public const string Conservative = "conservative";

    /// <summary>The name of the order in the control.</summary>
    public static string Option(string order) => order == Conservative ? "Conservative" : "Arena score";

    /// <summary>The clause that says which order the headline uses.</summary>
    public static string Clause(string order) => order == Conservative
        ? "the conservative order (lower bound of the arena score)"
        : "arena score";

    /// <summary>The note under the leaderboard table.</summary>
    public static string TableNote(string order) => order == Conservative
        ? "Ranked by the lower bound of the arena score (2.5th percentile), then arena score and rating; provisional models sort after the others."
        : "Ranked by arena score, then rating; provisional models sort after the others.";
}

/// <summary>
/// Pure derivations from the API DTOs (no I/O, no clock). The rules they carry are 03 §6.1 and RATING.md §5–§6.
/// </summary>
public static class ResultsModel
{
    /// <summary>Stacked outcome segments. Forfeit losses are a part of the losses, shown in their own slot.</summary>
    public const int WinsSlot = 2;
    public const int DrawsSlot = 0;
    public const int LossesSlot = 7;
    public const int ForfeitsSlot = 4;

    public static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Half the width of a 95 % interval: the "±" printed next to a rating.</summary>
    public static double HalfWidth(double low, double high) => (high - low) / 2;

    /// <summary>The leader's RQC against #2 is significant under the shared rule (RATING.md §5).</summary>
    public static bool SignificantlyAhead(PairwiseCoefficient pairwise, bool bothEstablished) =>
        Significance.IsSignificant(pairwise, bothEstablished);

    /// <summary>
    /// Whisker of each model's arena score: 100 × the mean of its pairwise RQC bounds. The API gives no interval for the
    /// arena score itself, so this is an approximation, and the chart description says so.
    /// </summary>
    public static IReadOnlyDictionary<string, (double Low, double High)> ArenaIntervals(MatrixDto matrix) =>
        matrix.Cells
            .GroupBy(cell => cell.A, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (Low: 100 * group.Average(c => c.RqcLow), High: 100 * group.Average(c => c.RqcHigh)),
                StringComparer.Ordinal);

    /// <summary>
    /// The whisker of a model's arena score: the bootstrap bounds the API sends (RATING.md §6). A payload without them
    /// gets the pairwise approximation of <see cref="ArenaIntervals"/>.
    /// </summary>
    public static (double Low, double High)? Whisker(ModelRating model, IReadOnlyDictionary<string, (double Low, double High)> approximations) =>
        model.ArenaScoreLow is { } low && model.ArenaScoreHigh is { } high
            ? (low, high)
            : approximations.TryGetValue(model.Model, out var approximation) ? approximation : null;

    /// <summary>
    /// The sentence under the headline. Null when there is no #2 to compare with. A lead that is not significant only
    /// because a model is provisional says so in the words of <see cref="Significance.TooFewGames"/>.
    /// </summary>
    public static string? Verdict(PairwiseCoefficient? pairwise, string? second, bool bothEstablished)
    {
        if (pairwise is null || second is null) return null;
        var significant = SignificantlyAhead(pairwise, bothEstablished);
        var reading = significant
            ? "Significantly ahead of"
            : "Not yet significantly ahead of";
        var sentence = $"{reading} #2 ({second}): RQC {Number(pairwise.Rqc, "0.00")}, " +
                       $"95 % interval {Number(pairwise.RqcLow, "0.00")} to {Number(pairwise.RqcHigh, "0.00")}.";
        return significant || bothEstablished ? sentence : $"{sentence} {Significance.TooFewGames}";
    }

    public static ResultsReport Build(ResultsInputs input)
    {
        var ranking = input.Board.Models;
        var order = ranking.Select(m => m.Model).ToList();
        var slots = ChartPalette.AssignSlots(input.AllModels);
        var intervals = ArenaIntervals(input.Matrix);
        var breakdown = input.Breakdown.ToDictionary(r => r.Model, StringComparer.Ordinal);

        var bars = ranking
            .Select((m, rank) =>
            {
                var interval = Whisker(m, intervals);
                return new RankedBar(m.Model, m.ArenaScore, interval?.Low, interval?.High,
                    Highlighted: rank == 0, Provisional: m.Provisional);
            })
            .ToList();

        // Palette slots go to the first eight models alphabetically, over every model ever rated (03 §6.1).
        var lines = new List<LineSeries>();
        foreach (var (model, slot) in slots.OrderBy(pair => pair.Value))
        {
            if (input.History.TryGetValue(model, out var points) && points.Count > 0)
                lines.Add(new LineSeries(model, slot, points.Select(p => new ChartPoint(p.RatedGames, p.Rating)).ToList()));
        }
        var folded = input.AllModels
            .Distinct(StringComparer.Ordinal)
            .Where(model => !slots.ContainsKey(model))
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToList();

        var heat = input.Matrix.Cells
            .Select(cell => new HeatCell(cell.A, cell.B, cell.Rqc, Muted: !Significance.IntervalShowsLead(cell.RqcLow, cell.RqcHigh)))
            .ToList();

        var withBreakdown = order.Where(breakdown.ContainsKey).Select(m => breakdown[m]).ToList();
        var outcomes = withBreakdown
            .Where(r => r.Games > 0)
            .Select(r => new StackRow(r.Model,
            [
                new StackSegment("Wins", WinsSlot, r.Wins),
                new StackSegment("Draws", DrawsSlot, r.Draws),
                new StackSegment("Losses", LossesSlot, r.Losses - r.ForfeitLosses),
                new StackSegment("Forfeit losses", ForfeitsSlot, r.ForfeitLosses),
            ]))
            .ToList();

        var legality = withBreakdown
            .Where(r => r.Moves > 0)
            .Select(r => new CategoryValue(r.Model, 100 * r.FirstAttemptLegalRate))
            .ToList();

        // Only shown when Stockfish has measured the moves; a model with no measured move has no bar.
        IReadOnlyList<CategoryValue>? acpl = input.Board.MoveQualityAvailable
            ? withBreakdown
                .Where(r => r.AverageCentipawnLoss is not null)
                .Select(r => new CategoryValue(r.Model, r.AverageCentipawnLoss!.Value))
                .ToList()
            : null;

        var scatter = ranking
            .Where(r => breakdown.ContainsKey(r.Model))
            .Select(r => new ScatterPoint(r.Model, breakdown[r.Model].AvgLatencyMs / 1000, r.Rating))
            .ToList();

        var mapModels = ranking
            .Select(r => MapModelOf(r, breakdown.GetValueOrDefault(r.Model), slots))
            .ToList();

        return new ResultsReport(
            ranking,
            bars,
            ranking.Count > 1 ? Verdict(input.LeaderVsSecond, order[1], Significance.BothEstablished(ranking, order[0], order[1])) : null,
            lines,
            folded,
            input.Matrix.Models,
            heat,
            outcomes,
            legality,
            acpl,
            scatter,
            mapModels,
            PromptStyles.Present(ranking.Select(r => r.Model)),
            PromptStyleComparison.Build(ranking, input.Matrix.Cells, breakdown));
    }

    /// <summary>
    /// One model of the map: its leaderboard numbers joined with its breakdown row. Tokens per move are the input plus the
    /// output tokens, and only when both are recorded (03 §13).
    /// </summary>
    public static MapModel MapModelOf(ModelRating model, ModelResultsDto? results, IReadOnlyDictionary<string, int> slots)
    {
        var tokens = results?.AvgInputTokens is { } input && results.AvgOutputTokens is { } output ? input + output : (double?)null;
        return new MapModel(
            model.Model,
            model.Games,
            model.Provisional,
            slots.TryGetValue(model.Model, out var slot) ? slot : null,
            model.Rating,
            model.CiLow,
            model.CiHigh,
            model.ArenaScore,
            model.ArenaScoreLow,
            model.ArenaScoreHigh,
            model.Legality,
            model.MoveQuality,
            results?.AvgLatencyMs / 1000,
            tokens,
            results?.AvgEstimatedUsdPerGame,
            results?.ForfeitRate);
    }
}
