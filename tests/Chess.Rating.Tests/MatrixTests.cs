using Chess.Rating;
using Xunit;

namespace Chess.Rating.Tests;

// RatingCalculator.Matrix and ComputeWithMatrix share one bootstrap for every cell. The cells must be the numbers
// Pairwise gives for the same pair, bit for bit: nothing published may move.
public class MatrixTests
{
    private static readonly string[] Models = ["random", "m1", "m2", "m3", "m4", "m5"];

    private static List<PlayerMoveStats> StatsOf(IEnumerable<string> models) =>
        models.Select((m, i) => new PlayerMoveStats(m, 100, 80 + i, m == "random" ? null : 20 + 7 * i)).ToList();

    [Fact]
    public void EveryCellEqualsPairwiseExactly()
    {
        var games = TestData.RandomGames(11, 240, Models);
        var report = RatingCalculator.Compute(games, StatsOf(Models), new RatingOptions());

        var cells = RatingCalculator.Matrix(report, games);

        Assert.Equal(Models.Length * (Models.Length - 1), cells.Count);
        foreach (var cell in cells)
        {
            Assert.Equal(RatingCalculator.Pairwise(report, games, cell.A, cell.B), cell);
        }
    }

    [Fact]
    public void CellsAreInReportOrderAMajorWithoutTheDiagonal()
    {
        var games = TestData.RandomGames(12, 120, Models);
        var report = RatingCalculator.Compute(games, StatsOf(Models), new RatingOptions());
        var order = RatingCalculator.Order(report, RatingOrder.Conservative);

        var cells = RatingCalculator.Matrix(order, games);

        var expected = order.Models.SelectMany(a => order.Models.Where(b => b.Model != a.Model).Select(b => (a.Model, b.Model)));
        Assert.Equal(expected, cells.Select(c => (c.A, c.B)));
    }

    [Fact]
    public void ComputeWithMatrixIsTheReportAndTheMatrix()
    {
        var games = TestData.RandomGames(13, 200, Models);
        var stats = StatsOf(Models);
        var options = new RatingOptions();

        var both = RatingCalculator.ComputeWithMatrix(games, stats, options);

        var report = RatingCalculator.Compute(games, stats, options);
        Assert.Equal(report.Models, both.Report.Models);
        Assert.Equal(report.WhiteAdvantage, both.Report.WhiteAdvantage);
        Assert.Equal(RatingCalculator.Matrix(report, games), both.Matrix);
        foreach (var cell in both.Matrix)
        {
            Assert.Equal(RatingCalculator.Pairwise(report, games, cell.A, cell.B), cell);
        }
    }

    [Fact]
    public void ComputeWithMatrixKeepsPairwiseDefaultsWhenTheOptionsDiffer()
    {
        // The report is bootstrapped with other options, but Pairwise always uses the defaults, so the cells must too.
        var games = TestData.RandomGames(14, 150, Models);
        var stats = StatsOf(Models);
        var options = new RatingOptions(BootstrapSamples: 40, Seed: 5, PriorSigma: 200, AnchorModel: "m1", AnchorRating: 100);

        var both = RatingCalculator.ComputeWithMatrix(games, stats, options);

        Assert.Equal(RatingCalculator.Compute(games, stats, options).Models, both.Report.Models);
        foreach (var cell in both.Matrix)
        {
            Assert.Equal(RatingCalculator.Pairwise(both.Report, games, cell.A, cell.B), cell);
        }
    }

    [Fact]
    public void CellsDoNotDependOnTheOrderOfTheGames()
    {
        var games = TestData.RandomGames(15, 100, Models);
        var report = RatingCalculator.Compute(games, StatsOf(Models), new RatingOptions());

        var shuffled = games.OrderBy(g => g.WhiteScore).ThenByDescending(g => g.Black).ThenBy(g => g.White).ToList();

        Assert.Equal(RatingCalculator.Matrix(report, games), RatingCalculator.Matrix(report, shuffled));
    }

    [Fact]
    public void AnEmptyReportHasNoCells()
    {
        var empty = RatingCalculator.ComputeWithMatrix([], [], new RatingOptions());

        Assert.Empty(empty.Report.Models);
        Assert.Empty(empty.Matrix);
        Assert.Empty(RatingCalculator.Matrix(empty.Report, []));
    }

    [Fact]
    public void ACancelledTokenStopsTheMatrixAndTheFit()
    {
        var games = TestData.RandomGames(16, 100, Models);
        var stats = StatsOf(Models);
        var report = RatingCalculator.Compute(games, stats, new RatingOptions());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => RatingCalculator.Matrix(report, games, cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => RatingCalculator.Compute(games, stats, new RatingOptions(), cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => RatingCalculator.ComputeWithMatrix(games, stats, new RatingOptions(), cancelled.Token));
    }

    [Fact]
    public void TheBootstrapIsRepeatableWhateverTheSchedule()
    {
        // Fits run in parallel and land at their own index: two runs must give the same intervals.
        var games = TestData.RandomGames(17, 180, Models);
        var stats = StatsOf(Models);

        var first = RatingCalculator.Compute(games, stats, new RatingOptions());
        var second = RatingCalculator.Compute(games, stats, new RatingOptions());

        Assert.Equal(first.Models, second.Models);
    }
}
