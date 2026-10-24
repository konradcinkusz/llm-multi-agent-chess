using Chess.Rating;
using Xunit;

namespace Chess.Rating.Tests;

// RATING.md §7, test vectors V1–V9.
public class RatingVectorTests
{
    private static readonly RatingOptions NoPriorNoAnchor = new(AnchorModel: null, PriorSigma: null, BootstrapSamples: 0);

    [Fact]
    public void V1_RatingGapMatchesSixToTwoRecordAndNoWhiteAdvantage()
    {
        var report = RatingCalculator.Compute(TestData.SixToTwo(), [], NoPriorNoAnchor);

        var gap = TestData.Row(report, "A").Rating - TestData.Row(report, "B").Rating;
        Assert.InRange(gap, 400 * Math.Log10(3) - 0.5, 400 * Math.Log10(3) + 0.5);
        Assert.InRange(report.WhiteAdvantage, -1, 1);
    }

    [Fact]
    public void V2_RelativeCoefficientWithEqualQualityIs0_6875()
    {
        var stats = new[] { new PlayerMoveStats("A", 10, 10, null), new PlayerMoveStats("B", 10, 10, null) };
        var games = TestData.SixToTwo();
        var report = RatingCalculator.Compute(games, stats, NoPriorNoAnchor);

        var pair = RatingCalculator.Pairwise(report, games, "A", "B");

        Assert.InRange(pair.Rqc, 0.6875 - 0.002, 0.6875 + 0.002);
        Assert.InRange(pair.ExpectedScore, 0.75 - 1e-6, 0.75 + 1e-6);
        Assert.InRange(pair.QualityShare, 0.5 - 1e-12, 0.5 + 1e-12);
    }

    [Fact]
    public void V3_RelativeCoefficientIsAntisymmetricAndInsideUnitInterval()
    {
        var stats = new[] { new PlayerMoveStats("A", 10, 9, null), new PlayerMoveStats("B", 10, 6, null) };
        var games = TestData.SixToTwo();
        var report = RatingCalculator.Compute(games, stats, NoPriorNoAnchor);

        var ab = RatingCalculator.Pairwise(report, games, "A", "B");
        var ba = RatingCalculator.Pairwise(report, games, "B", "A");

        Assert.InRange(ab.Rqc + ba.Rqc, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(ab.Rqc, 0, 1);
        Assert.True(ab.Rqc > 0.5, "A has the better record and better legality, so A must be above 0.5");
    }

    [Fact]
    public void V4_RandomIsAnchoredAtZeroAndAPerfectRecordStaysFinite()
    {
        var games = new List<GameOutcome>();
        for (var i = 0; i < 5; i++)
        {
            games.Add(new GameOutcome("A", "random", 1));
            games.Add(new GameOutcome("random", "A", 0));
        }

        var report = RatingCalculator.Compute(games, [], new RatingOptions(BootstrapSamples: 0));

        Assert.InRange(TestData.Row(report, "random").Rating, -1e-9, 1e-9);
        var a = TestData.Row(report, "A").Rating;
        Assert.True(double.IsFinite(a) && a > 400, $"expected a finite rating above 400, was {a}");
    }

    [Fact]
    public void V5_ReversedGameOrderGivesIdenticalReport()
    {
        var games = TestData.RandomGames(seed: 7, count: 300, models: ["A", "B", "C", "random"]);
        var stats = new[] { new PlayerMoveStats("A", 40, 38, 80.5), new PlayerMoveStats("B", 40, 31, null) };

        var forward = RatingCalculator.Compute(games, stats, new RatingOptions());
        var reversed = RatingCalculator.Compute(Enumerable.Reverse(games).ToList(), stats, new RatingOptions());

        Assert.Equal(forward.Games, reversed.Games);
        Assert.Equal(forward.WhiteAdvantage, reversed.WhiteAdvantage);
        Assert.Equal(forward.Models, reversed.Models);
    }

    [Fact]
    public void V6_SameSeedGivesSameIntervalsOnEveryRun()
    {
        var games = TestData.RandomGames(seed: 3, count: 200, models: ["A", "B", "C"]);

        var first = RatingCalculator.Compute(games, [], new RatingOptions());
        var second = RatingCalculator.Compute(games, [], new RatingOptions());

        Assert.True(first.Models.Any(m => m.CiHigh > m.CiLow), "the bootstrap must produce non-trivial intervals");
        Assert.Equal(first.Models.Select(m => (m.CiLow, m.CiHigh)), second.Models.Select(m => (m.CiLow, m.CiHigh)));
    }

    [Fact]
    public void V7_TenGamesMakeAModelProvisional()
    {
        var games = new List<GameOutcome>();
        for (var i = 0; i < 5; i++)
        {
            games.Add(new GameOutcome("A", "B", 1));
            games.Add(new GameOutcome("B", "A", 0));
        }

        var report = RatingCalculator.Compute(games, [], new RatingOptions(BootstrapSamples: 0));

        Assert.True(TestData.Row(report, "A").Provisional);
        Assert.Equal(10, TestData.Row(report, "A").Games);
    }

    [Fact]
    public void V8_QualityIsTheBlendOfMoveQualityAndLegality()
    {
        var games = new List<GameOutcome> { new("A", "B", 0.5) };
        var stats = new[] { new PlayerMoveStats("A", 10, 5, 100), new PlayerMoveStats("B", 10, 10, null) };

        var report = RatingCalculator.Compute(games, stats, TestData.NoBootstrap);
        var a = TestData.Row(report, "A");

        Assert.InRange(a.Legality, 0.5 - 1e-12, 0.5 + 1e-12);
        Assert.InRange(a.MoveQuality!.Value, 0.5 - 1e-12, 0.5 + 1e-12);
        Assert.InRange(a.Quality, 0.5 - 1e-9, 0.5 + 1e-9);
    }

    [Fact]
    public void V9_WithoutStockfishQualityIsLegalityAndMoveQualityIsNull()
    {
        var games = new List<GameOutcome> { new("A", "B", 0.5) };
        var stats = new[] { new PlayerMoveStats("A", 10, 8, null), new PlayerMoveStats("B", 10, 10, null) };

        var report = RatingCalculator.Compute(games, stats, TestData.NoBootstrap);
        var a = TestData.Row(report, "A");

        Assert.InRange(a.Quality, 0.8 - 1e-12, 0.8 + 1e-12);
        Assert.Null(a.MoveQuality);
    }
}
