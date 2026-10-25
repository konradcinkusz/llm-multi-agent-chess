using Chess.Api.Benchmarks;
using Chess.Api.Contracts;
using Chess.Rating;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

public class BenchmarkReportTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("5c1a9e44-2b7d-4f0e-9a51-7d3e0b8c2f10");

    private static readonly BenchmarkFingerprintDto Fingerprint = new("1.0.0+abc1234", "1", "sha256:0123456789ab", At);
    private static readonly RatingReport NoRating = new([], 0, 0);

    private static readonly SideUsageDto NoCost = new("white", "x", 0, 0, 0, 0, 0, null, null, "unknown");

    private static SideUsageDto Usage(string color, double? usd, long input = 100, long output = 50) =>
        new(color, "x", 1, input, output, 0, 0, null, usd, "estimated");

    private static BenchmarkGame Game(string opponent, bool candidateWhite, string result, string? termination = null,
        int moves = 10, int firstTry = 9, double? usd = 0.01) =>
        new(opponent, candidateWhite, result, termination, moves, firstTry, Usage("c", usd), Usage("o", null));

    private static BenchmarkDto Run(params string[] opponents) =>
        new(Id, "Pick", "claude@medium", opponents, 4, "finished", opponents.Length * 4, opponents.Length * 4, At, At);

    [Fact]
    public void AWinOfSeventyFivePercentIsAboutPlusOneHundredNinetyOneWithAnIntervalAroundIt()
    {
        var scores = Enumerable.Repeat(1d, 30).Concat(Enumerable.Repeat(0d, 10)).ToList();

        var (diff, low, high) = BenchmarkReportBuilder.EloDifference(scores);

        Assert.NotNull(diff);
        Assert.Equal(190.85, diff!.Value, 2);
        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(low < diff && diff < high, $"{low} < {diff} < {high}");
    }

    [Fact]
    public void AScoreOfOneHasNoFiniteDifferenceOrInterval()
    {
        var (diff, low, high) = BenchmarkReportBuilder.EloDifference(Enumerable.Repeat(1d, 10).ToList());

        Assert.Null(diff);
        Assert.Null(low);
        Assert.Null(high);
    }

    [Fact]
    public void NoDecidedGamesGiveNoDifference()
    {
        Assert.Equal((null, null, null), BenchmarkReportBuilder.EloDifference([]));
    }

    [Fact]
    public void TheTotalsOfTheReportEqualItsGames()
    {
        var games = new List<BenchmarkGame>
        {
            Game("fake-b", true, "1-0", "checkmate"),
            Game("fake-b", false, "1-0", "checkmate"),
            Game("fake-b", true, "1/2-1/2", "stalemate"),
            Game("fake-b", false, "1-0", "forfeit-black"),
            Game("random", true, "0-1", "checkmate"),
            Game("random", false, "0-1", "checkmate"),
            Game("random", true, "1-0", "checkmate"),
            Game("random", false, "*", "aborted"),
        };

        var report = BenchmarkReportBuilder.Build(Run("fake-b", "random"), games, NoRating, [], Fingerprint);

        Assert.Equal(7, report.Summary.Games);
        Assert.Equal(report.Summary.Games, report.Opponents.Sum(o => o.Games));
        var fakeB = report.Opponents.Single(o => o.Opponent == "fake-b");
        Assert.Equal((4, 1, 1, 2), (fakeB.Games, fakeB.Wins, fakeB.Draws, fakeB.Losses));
        Assert.Equal(0.375, fakeB.Score);
        var random = report.Opponents.Single(o => o.Opponent == "random");
        Assert.Equal((3, 2, 0, 1), (random.Games, random.Wins, random.Draws, random.Losses));
        Assert.Equal(report.Summary.Games, report.Opponents.Sum(o => o.Wins + o.Draws + o.Losses));
    }

    [Fact]
    public void AForfeitIsCountedOnlyWhenTheCandidateForfeited()
    {
        var games = new List<BenchmarkGame>
        {
            Game("fake-b", true, "0-1", "forfeit-white"),
            Game("fake-b", false, "0-1", "forfeit-white"),
        };

        var fakeB = BenchmarkReportBuilder.Build(Run("fake-b"), games, NoRating, [], Fingerprint).Opponents.Single();

        Assert.Equal(1, fakeB.ForfeitLosses);
        Assert.Equal(1, fakeB.Losses);
        Assert.Equal(1, fakeB.Wins);
    }

    [Fact]
    public void AnAbortedGameIsLeftOutOfTheScoreButItsCostCounts()
    {
        var games = new List<BenchmarkGame>
        {
            Game("fake-b", true, "1-0", "checkmate", usd: 0.5),
            Game("fake-b", true, "*", "aborted", usd: 0.25),
        };

        var fakeB = BenchmarkReportBuilder.Build(Run("fake-b"), games, NoRating, [], Fingerprint).Opponents.Single();

        Assert.Equal(1, fakeB.Games);
        Assert.Equal(1d, fakeB.Score);
        Assert.Equal(0.75, fakeB.CandidateEstimatedUsd);
    }

    [Fact]
    public void TheFirstTryLegalRateIsTheShareOfMovesLegalAtOnce()
    {
        var games = new List<BenchmarkGame> { Game("fake-b", true, "1-0", moves: 8, firstTry: 6) };

        var fakeB = BenchmarkReportBuilder.Build(Run("fake-b"), games, NoRating, [], Fingerprint).Opponents.Single();

        Assert.Equal(0.75, fakeB.CandidateFirstAttemptLegalRate, 6);
    }

    [Fact]
    public void TheSummaryHasNoRatingForACandidateTheGlobalFitDoesNotKnow()
    {
        var report = BenchmarkReportBuilder.Build(Run("fake-b"), [Game("fake-b", true, "1-0")], NoRating, [], Fingerprint);

        Assert.Null(report.Summary.Rating);
        Assert.True(report.Summary.Provisional);
        Assert.Equal(Fingerprint, report.Fingerprint);
    }

    [Fact]
    public void TheCandidateTokensAreItsInputAndOutputTokensOverAllItsGames()
    {
        var report = BenchmarkReportBuilder.Build(Run("fake-b"), [Game("fake-b", true, "1-0"), Game("fake-b", false, "0-1")],
            NoRating, [], Fingerprint);

        Assert.Equal(300, report.Summary.CandidateTokens);
        Assert.Equal(0.02, report.Summary.CandidateEstimatedUsd!.Value, 6);
        // The opponent's side has no known cost, so the total is the candidate's alone.
        Assert.Equal(0.02, report.Summary.TotalEstimatedUsd!.Value, 6);
    }
}
