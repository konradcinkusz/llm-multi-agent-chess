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

/// <summary>Chart inputs and sentences of the seven sections of 03 §6.1, in the order the page shows them.</summary>
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
    IReadOnlyList<ScatterPoint> Scatter);

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
                var interval = intervals.TryGetValue(m.Model, out var iv) ? iv : (((double Low, double High)?)null);
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
            scatter);
    }
}
