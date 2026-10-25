using Chess.Rating;
using Xunit;

namespace Chess.Rating.Tests;

// R1 "Done when" and RATING.md §6–§7 (V10–V13): the arena interval and the conservative order.
public class ConservativeOrderTests
{
    private static readonly string[] Field = ["A", "B", "C"];

    [Fact]
    public void V10_TheArenaIntervalContainsThePointScoreOfEveryModel()
    {
        var games = TestData.RandomGames(seed: 7, count: 300, models: ["A", "B", "C", "D", "E"]);

        var report = RatingCalculator.Compute(games, [], new RatingOptions());

        Assert.Equal(5, report.Models.Count);
        foreach (var row in report.Models)
        {
            var (low, high) = Bounds(row);
            Assert.True(low < high, $"{row.Model} interval must have a positive width");
            Assert.InRange(row.ArenaScore, low, high);
        }
    }

    [Fact]
    public void V11_AModelWithFewerGamesHasAWiderArenaInterval()
    {
        // Few and Many have the same strength (both score at random against the same field), so only the sample size differs.
        var games = TestData.RandomGames(seed: 3, count: 300, models: Field)
            .Concat(Versus("Few", 30, seed: 4))
            .Concat(Versus("Many", 300, seed: 5))
            .ToList();

        var report = RatingCalculator.Compute(games, [], new RatingOptions());

        var few = Bounds(TestData.Row(report, "Few"));
        var many = Bounds(TestData.Row(report, "Many"));
        Assert.True(few.High - few.Low > many.High - many.Low,
            $"Few [{few.Low:F2}, {few.High:F2}] must be wider than Many [{many.Low:F2}, {many.High:F2}]");
    }

    [Fact]
    public void V12_WithEqualArenaScoresTheModelWithMoreGamesRanksFirstInTheConservativeOrder()
    {
        // Many has the lower rating but the higher lower bound: with the same point score it has more games.
        var few = Row("Few", arena: 50, low: 40, rating: 300, games: 30);
        var many = Row("Many", arena: 50, low: 46, rating: 100, games: 300);
        var report = new RatingReport([few, many], 0, 0);

        var conservative = RatingCalculator.Order(report, RatingOrder.Conservative);
        var score = RatingCalculator.Order(report, RatingOrder.Score);

        Assert.Equal(new[] { "Many", "Few" }, conservative.Models.Select(m => m.Model));
        Assert.Equal(new[] { "Few", "Many" }, score.Models.Select(m => m.Model));
    }

    [Fact]
    public void V13_TheSameSeedGivesIdenticalArenaIntervals()
    {
        var games = TestData.RandomGames(seed: 9, count: 200, models: ["A", "B", "C", "D"]);

        var first = RatingCalculator.Compute(games, [], new RatingOptions(Seed: 42));
        var second = RatingCalculator.Compute(games, [], new RatingOptions(Seed: 42));

        Assert.Equal(first.Models, second.Models);
        Assert.All(first.Models, row => Assert.NotNull(row.ArenaScoreLow));
    }

    [Fact]
    public void TheScoreOrderIsTheOrderComputeReturns()
    {
        var games = TestData.RandomGames(seed: 11, count: 250, models: ["A", "B", "C", "D"]);
        var report = RatingCalculator.Compute(games, [], new RatingOptions(BootstrapSamples: 20));

        var ordered = RatingCalculator.Order(report, RatingOrder.Score);

        Assert.Equal(report.Models, ordered.Models);
    }

    [Fact]
    public void TheConservativeOrderOnlyReordersTheSameModels()
    {
        var games = TestData.RandomGames(seed: 13, count: 250, models: ["A", "B", "C", "D"]);
        var report = RatingCalculator.Compute(games, [], new RatingOptions(BootstrapSamples: 20));

        var ordered = RatingCalculator.Order(report, RatingOrder.Conservative);

        Assert.Equal(report.Models.OrderBy(m => m.Model, StringComparer.Ordinal), ordered.Models.OrderBy(m => m.Model, StringComparer.Ordinal));
        Assert.Equal(report.WhiteAdvantage, ordered.WhiteAdvantage);
        Assert.Equal(report.Games, ordered.Games);
    }

    [Fact]
    public void ProvisionalModelsSortAfterEstablishedOnesInBothOrders()
    {
        var provisional = Row("Young", arena: 80, low: 75, rating: 500, provisional: true);
        var established = Row("Old", arena: 40, low: 35, rating: 0);
        var report = new RatingReport([provisional, established], 0, 0);

        Assert.Equal(new[] { "Old", "Young" }, RatingCalculator.Order(report, RatingOrder.Conservative).Models.Select(m => m.Model));
        Assert.Equal(new[] { "Old", "Young" }, RatingCalculator.Order(report, RatingOrder.Score).Models.Select(m => m.Model));
    }

    [Fact]
    public void AnUnknownOrderIsRejected()
    {
        var report = new RatingReport([Row("A", arena: 50, low: 45, rating: 0)], 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => RatingCalculator.Order(report, (RatingOrder)99));
    }

    private static (double Low, double High) Bounds(ModelRating row) =>
        (row.ArenaScoreLow ?? throw new InvalidOperationException($"{row.Model} has no arena bounds."),
         row.ArenaScoreHigh ?? throw new InvalidOperationException($"{row.Model} has no arena bounds."));

    private static ModelRating Row(string model, double arena, double low, double rating, bool provisional = false, int games = 100) =>
        new(model, rating, rating - 50, rating + 50, games, Score: 0.5, Legality: 1, MoveQuality: null, Quality: 1,
            ArenaScore: arena, Provisional: provisional, ArenaScoreLow: low, ArenaScoreHigh: arena + (arena - low));

    // Seeded games of one model against the field, half as White. Its strength is that of the random field.
    private static IEnumerable<GameOutcome> Versus(string model, int count, int seed)
    {
        var random = new Random(seed);
        double[] scores = [0, 0.5, 1];
        for (var i = 0; i < count; i++)
        {
            var opponent = Field[random.Next(Field.Length)];
            var score = scores[random.Next(scores.Length)];
            yield return i % 2 == 0 ? new GameOutcome(model, opponent, score) : new GameOutcome(opponent, model, score);
        }
    }
}
