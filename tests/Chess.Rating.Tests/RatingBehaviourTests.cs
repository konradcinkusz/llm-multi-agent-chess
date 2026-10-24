using System.Diagnostics;
using Chess.Rating;
using Xunit;

namespace Chess.Rating.Tests;

// T1.6 "Done when" criteria beyond the RATING.md vectors, and the rules of §5 and §6.
public class RatingBehaviourTests
{
    [Fact]
    public void EmptyInputGivesEmptyReport()
    {
        var report = RatingCalculator.Compute([], [], new RatingOptions());

        Assert.Empty(report.Models);
        Assert.Equal(0, report.WhiteAdvantage);
        Assert.Equal(0, report.Games);
    }

    [Fact]
    public void ModelWithGamesOnlyAsWhiteStillGetsFiniteRatingAndInterval()
    {
        var games = new List<GameOutcome>
        {
            new("A", "B", 1), new("A", "B", 1), new("A", "B", 0.5), new("A", "C", 0), new("A", "C", 1),
        };

        var report = RatingCalculator.Compute(games, [], new RatingOptions());

        foreach (var row in report.Models)
        {
            Assert.True(double.IsFinite(row.Rating), $"{row.Model} rating must be finite");
            Assert.True(double.IsFinite(row.CiLow) && double.IsFinite(row.CiHigh), $"{row.Model} interval must be finite");
        }
    }

    [Fact]
    public void WithoutRandomTheMeanRatingIsAnchoredAtZero()
    {
        var games = TestData.RandomGames(seed: 11, count: 150, models: ["A", "B", "C"]);

        var report = RatingCalculator.Compute(games, [], new RatingOptions(BootstrapSamples: 0));

        var mean = report.Models.Average(m => m.Rating);
        Assert.InRange(mean, -1e-9, 1e-9);
    }

    [Fact]
    public void TwoHundredModelsOverFiveThousandGamesComputeWithinTwoSecondsWithoutBootstrap()
    {
        var models = Enumerable.Range(0, 200).Select(i => $"m{i:D3}").ToArray();
        var games = TestData.RandomGames(seed: 42, count: 5000, models: models);

        var timer = Stopwatch.StartNew();
        var report = RatingCalculator.Compute(games, [], TestData.NoBootstrap);
        timer.Stop();

        Assert.Equal(200, report.Models.Count);
        Assert.True(timer.ElapsedMilliseconds < 2000, $"took {timer.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void IntervalsBracketThePointRating()
    {
        var games = TestData.RandomGames(seed: 5, count: 300, models: ["A", "B", "C", "random"]);

        var report = RatingCalculator.Compute(games, [], new RatingOptions());

        foreach (var row in report.Models)
        {
            Assert.True(row.CiLow <= row.Rating && row.Rating <= row.CiHigh,
                $"{row.Model}: {row.CiLow} <= {row.Rating} <= {row.CiHigh} does not hold");
        }
    }

    [Fact]
    public void ScoreCountsDrawsAsHalfAndGivesTheMeanOverRatedGames()
    {
        var games = new List<GameOutcome> { new("A", "B", 1), new("B", "A", 1), new("A", "B", 0.5) };

        var report = RatingCalculator.Compute(games, [], TestData.NoBootstrap);

        // A scores 1 (win as White), 0 (loss as Black), ½ (draw as White): 1.5 of 3.
        Assert.Equal(0.5, TestData.Row(report, "A").Score, 12);
        Assert.Equal(3, TestData.Row(report, "A").Games);
    }

    [Fact]
    public void ArenaScoreIsOneHundredTimesTheMeanRqcAgainstOthers()
    {
        var stats = new[] { new PlayerMoveStats("A", 10, 10, null), new PlayerMoveStats("B", 10, 10, null) };
        var games = TestData.SixToTwo();
        var report = RatingCalculator.Compute(games, stats, new RatingOptions(AnchorModel: null, PriorSigma: null, BootstrapSamples: 0));

        // RQC(A,B) = 0.6875 (RATING.md V2), so AS_A = 68.75 and AS_B = 31.25.
        Assert.InRange(TestData.Row(report, "A").ArenaScore, 68.75 - 0.2, 68.75 + 0.2);
        Assert.InRange(TestData.Row(report, "B").ArenaScore, 31.25 - 0.2, 31.25 + 0.2);
    }

    [Fact]
    public void LeaderboardIsOrderedByProvisionalThenArenaScoreThenRating()
    {
        var games = TestData.RandomGames(seed: 21, count: 400, models: ["A", "B", "C", "D"]);

        var rows = RatingCalculator.Compute(games, [], new RatingOptions()).Models;

        for (var i = 1; i < rows.Count; i++)
        {
            var previous = rows[i - 1];
            var current = rows[i];
            Assert.True(!previous.Provisional || current.Provisional, "provisional models must come last");
            if (previous.Provisional == current.Provisional)
            {
                Assert.True(previous.ArenaScore >= current.ArenaScore, "arena score must not increase down the board");
            }
        }
    }

    [Fact]
    public void PairwiseHeadToHeadIsTheDirectRecordNotTheCoefficient()
    {
        var games = new List<GameOutcome> { new("A", "B", 1), new("B", "A", 1), new("A", "B", 0.5), new("B", "A", 0.5) };
        var report = RatingCalculator.Compute(games, [], TestData.NoBootstrap);

        var pair = RatingCalculator.Pairwise(report, games, "A", "B");

        Assert.Equal(4, pair.HeadToHeadGames);
        Assert.Equal(0.5, pair.HeadToHeadScore);
    }

    [Fact]
    public void PairwiseEloEquivalentMatchesTheRqc()
    {
        // Same setup as RATING.md V2, so RQC = 0.6875 and the gap is 400·log10(3).
        var stats = new[] { new PlayerMoveStats("A", 10, 10, null), new PlayerMoveStats("B", 10, 10, null) };
        var games = TestData.SixToTwo();
        var report = RatingCalculator.Compute(games, stats, new RatingOptions(AnchorModel: null, PriorSigma: null, BootstrapSamples: 0));

        var pair = RatingCalculator.Pairwise(report, games, "A", "B");

        // Δ* = 400·log10(RQC / (1 − RQC)) = 400·log10(2.2) ≈ 136.97 for RQC = 0.6875.
        Assert.InRange(pair.EloEquivalent, 136.96, 136.98);
    }

    [Fact]
    public void PairwiseRejectsAModelNotInTheReport()
    {
        var games = TestData.SixToTwo();
        var report = RatingCalculator.Compute(games, [], TestData.NoBootstrap);

        Assert.Throws<ArgumentException>(() => RatingCalculator.Pairwise(report, games, "A", "Z"));
    }

    [Fact]
    public void DuplicateMoveStatisticsAreRejected()
    {
        var stats = new[] { new PlayerMoveStats("A", 10, 10, null), new PlayerMoveStats("A", 5, 5, null) };

        Assert.Throws<ArgumentException>(() => RatingCalculator.Compute(TestData.SixToTwo(), stats, TestData.NoBootstrap));
    }
}
