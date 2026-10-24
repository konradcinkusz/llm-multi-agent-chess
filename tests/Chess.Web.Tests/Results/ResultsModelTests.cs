using Chess.Web.Api;
using Chess.Web.Components.Charts;
using Chess.Web.Components.Pages.Results;
using Xunit;

namespace Chess.Web.Tests.Results;

public class ResultsModelTests
{
    [Fact]
    public void ArenaWhiskersAreTheMeanOfThePairwiseBoundsOnTheArenaScale()
    {
        var matrix = new MatrixDto(["alpha", "beta"],
        [
            Pair("alpha", "beta", 0.6, 0.4, 0.8),
            Pair("beta", "alpha", 0.4, 0.2, 0.6),
        ]);

        var intervals = ResultsModel.ArenaIntervals(matrix);

        // Each model has one cell here, so the mean is that cell's bound, scaled to the arena score (×100).
        Assert.Equal(40, intervals["alpha"].Low, 6);
        Assert.Equal(80, intervals["alpha"].High, 6);
        Assert.Equal(20, intervals["beta"].Low, 6);
        Assert.Equal(60, intervals["beta"].High, 6);
    }

    [Fact]
    public void TheVerdictIsNotSignificantWhenTheIntervalContainsOneHalf()
    {
        var verdict = ResultsModel.Verdict(Pair("alpha", "beta", 0.58, 0.45, 0.70), "beta");

        Assert.StartsWith("Not yet significantly ahead of #2 (beta)", verdict);
    }

    [Fact]
    public void TheVerdictIsSignificantWhenTheWholeIntervalIsAboveOneHalf()
    {
        var verdict = ResultsModel.Verdict(Pair("alpha", "beta", 0.7, 0.55, 0.82), "beta");

        Assert.StartsWith("Significantly ahead of #2 (beta)", verdict);
    }

    [Fact]
    public void AnIntervalThatStartsExactlyAtOneHalfIsNotSignificant()
    {
        // RQC_low = 0.5 leaves 0.5 inside the interval, so the lead is not yet significant.
        Assert.False(ResultsModel.SignificantlyAhead(Pair("alpha", "beta", 0.6, 0.5, 0.7)));
    }

    [Fact]
    public void ThereIsNoVerdictWithoutASecondModel()
    {
        var report = Build(
            models: [Model("alpha", 120, 66)],
            pair: null);

        Assert.Null(report.Verdict);
    }

    [Fact]
    public void OnlyTheLeaderBarIsHighlightedAndProvisionalBarsAreFlagged()
    {
        var report = Build(
            models: [Model("alpha", 120, 66), Model("beta", 90, 40, provisional: true)],
            pair: Pair("alpha", "beta", 0.6, 0.52, 0.68));

        Assert.Equal(["alpha", "beta"], report.Bars.Select(b => b.Label));
        Assert.True(report.Bars[0].Highlighted);
        Assert.False(report.Bars[1].Highlighted);
        Assert.True(report.Bars[1].Provisional);
    }

    [Fact]
    public void AModelWithoutMatrixCellsHasNoWhisker()
    {
        var report = Build(
            models: [Model("alpha", 120, 66), Model("solo", 90, 40)],
            pair: null,
            cells: [Pair("alpha", "solo", 0.6, 0.52, 0.68)]);

        Assert.NotNull(report.Bars[0].Low);
        Assert.Null(report.Bars[1].Low);
        Assert.Null(report.Bars[1].High);
    }

    [Fact]
    public void HeatCellsAreMutedOnlyWhenTheirIntervalContainsOneHalf()
    {
        var report = Build(
            models: [Model("alpha", 120, 66), Model("beta", 90, 40)],
            pair: null,
            cells:
            [
                Pair("alpha", "beta", 0.6, 0.52, 0.68),
                Pair("beta", "alpha", 0.4, 0.30, 0.55),
            ]);

        Assert.False(report.Heat[0].Muted);
        Assert.True(report.Heat[1].Muted);
    }

    [Fact]
    public void ForfeitLossesAreCountedOnceInTheOutcomeBar()
    {
        // Losses already includes the forfeits: 8 losses of which 1 is a forfeit leaves 7 plain losses.
        var report = Build(
            models: [Model("alpha", 120, 66)],
            pair: null,
            breakdown: [Results("alpha", games: 30, wins: 20, draws: 2, losses: 8, forfeits: 1)]);

        var segments = report.Outcomes.Single().Segments;

        Assert.Equal([20, 2, 7, 1], segments.Select(s => s.Value));
        Assert.Equal(30, segments.Sum(s => s.Value));
    }

    [Fact]
    public void PaletteSlotsDoNotChangeWhenTheScopeNarrows()
    {
        var history = new Dictionary<string, IReadOnlyList<RatingHistoryPointDto>>(StringComparer.Ordinal)
        {
            ["beta"] = [new(DateTimeOffset.UnixEpoch, 10, "beta", 90, 80, 100, 40, false)],
        };
        var all = new[] { "alpha", "beta", "gamma" };

        var full = Build(models: [Model("alpha", 120, 66), Model("beta", 90, 40)], all: all, history: history);
        var scoped = Build(models: [Model("beta", 90, 40)], all: all, history: history);

        Assert.Equal(1, full.Lines.Single().Slot);
        Assert.Equal(1, scoped.Lines.Single().Slot);
    }

    [Fact]
    public void ModelsBeyondTheEightPaletteSlotsAreFolded()
    {
        var all = Enumerable.Range(0, 9).Select(i => $"model-{i}").ToArray();

        var report = Build(models: [Model("model-0", 120, 66)], all: all);

        Assert.Equal(["model-8"], report.Folded);
    }

    [Fact]
    public void TheMoveQualityChartIsHiddenWithoutEngineData()
    {
        var report = Build(
            models: [Model("alpha", 120, 66)],
            pair: null,
            breakdown: [Results("alpha", games: 4, wins: 3, draws: 0, losses: 1, forfeits: 0, acpl: 35)],
            moveQualityAvailable: false);

        Assert.Null(report.Acpl);
        Assert.Equal(90, report.Legality.Single().Value, 6);
    }

    [Fact]
    public void TheScatterPlotsRatingAgainstLatencyInSeconds()
    {
        var report = Build(
            models: [Model("alpha", 120, 66)],
            pair: null,
            breakdown: [Results("alpha", games: 4, wins: 3, draws: 0, losses: 1, forfeits: 0, latencyMs: 2500)]);

        var point = report.Scatter.Single();
        Assert.Equal(2.5, point.X, 6);
        Assert.Equal(120, point.Y, 6);
    }

    private static ModelRating Model(string name, double rating, double arenaScore, bool provisional = false) =>
        new(name, rating, rating - 20, rating + 20, 30, 0.6, 0.98, null, 0.9, arenaScore, provisional);

    private static PairwiseCoefficient Pair(string a, string b, double rqc, double low, double high) =>
        new(a, b, 0.5, 0.5, rqc, low, high, 0, 10, 0.5);

    private static ModelResultsDto Results(string model, int games, int wins, int draws, int losses, int forfeits,
        double? acpl = null, double latencyMs = 1000) =>
        new(model, games, wins, draws, losses, forfeits, 100, 0.9, latencyMs, null, null, acpl);

    private static ResultsReport Build(
        ModelRating[] models,
        PairwiseCoefficient? pair = null,
        PairwiseCoefficient[]? cells = null,
        ModelResultsDto[]? breakdown = null,
        string[]? all = null,
        IReadOnlyDictionary<string, IReadOnlyList<RatingHistoryPointDto>>? history = null,
        bool moveQualityAvailable = false)
    {
        var names = models.Select(m => m.Model).ToArray();
        var board = new LeaderboardDto(models, 0, 40, moveQualityAvailable);
        return ResultsModel.Build(new ResultsInputs(
            board,
            all ?? names,
            new MatrixDto(names, cells ?? []),
            breakdown ?? [],
            pair,
            history ?? new Dictionary<string, IReadOnlyList<RatingHistoryPointDto>>()));
    }
}
