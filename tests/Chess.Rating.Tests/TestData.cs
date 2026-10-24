using Chess.Rating;

namespace Chess.Rating.Tests;

internal static class TestData
{
    // Fixed options used where a vector needs no bootstrap: the RATING.md defaults with no samples.
    public static RatingOptions NoBootstrap => new(BootstrapSamples: 0);

    // A vs B, A wins 6 of 8: three of each colour's wins, one loss per colour (RATING.md V1).
    public static List<GameOutcome> SixToTwo() =>
    [
        new("A", "B", 1), new("A", "B", 1), new("A", "B", 1), new("A", "B", 0),
        new("B", "A", 0), new("B", "A", 0), new("B", "A", 0), new("B", "A", 1),
    ];

    public static ModelRating Row(RatingReport report, string model) =>
        report.Models.Single(m => m.Model == model);

    // Seeded, so every test run sees the same games.
    public static List<GameOutcome> RandomGames(int seed, int count, string[] models)
    {
        var random = new Random(seed);
        double[] scores = [0, 0.5, 1];
        var games = new List<GameOutcome>(count);
        for (var i = 0; i < count; i++)
        {
            var white = models[random.Next(models.Length)];
            var black = models[random.Next(models.Length)];
            while (black == white)
            {
                black = models[random.Next(models.Length)];
            }

            games.Add(new GameOutcome(white, black, scores[random.Next(scores.Length)]));
        }

        return games;
    }
}
