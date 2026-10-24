using Chess.Web.Api;
using Chess.Web.Components.Pages.HeadToHead;
using Xunit;

namespace Chess.Web.Tests.HeadToHead;

public class DirectGamesTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BetweenKeepsOnlyTheGamesOfThePairInEitherColour()
    {
        var asWhite = Game(white: "gpt", black: "claude", Day);
        var asBlack = Game(white: "claude", black: "gpt", Day.AddHours(1));
        var other = Game(white: "gpt", black: "random", Day.AddHours(2));
        var unrelated = Game(white: "claude", black: "random", Day.AddHours(3));

        var pairGames = DirectGames.Between([asWhite, other, asBlack, unrelated], "gpt", "claude");

        Assert.Equal(new[] { asBlack, asWhite }, pairGames);
    }

    [Fact]
    public void BetweenIsNewestFirst()
    {
        var older = Game(white: "gpt", black: "claude", Day);
        var newer = Game(white: "claude", black: "gpt", Day.AddDays(2));

        Assert.Equal(new[] { newer, older }, DirectGames.Between(new[] { older, newer }, "gpt", "claude"));
    }

    [Fact]
    public void BetweenIsEmptyWhenTheModelsNeverMet()
    {
        var games = new[] { Game(white: "gpt", black: "random", Day) };

        Assert.Empty(DirectGames.Between(games, "gpt", "claude"));
    }

    [Theory]
    [InlineData("1-0", "gpt", 1.0)]
    [InlineData("1-0", "claude", 0.0)]
    [InlineData("0-1", "gpt", 0.0)]
    [InlineData("0-1", "claude", 1.0)]
    [InlineData("1/2-1/2", "gpt", 0.5)]
    [InlineData("1/2-1/2", "claude", 0.5)]
    public void ScoreForFinishedGamesFollowsTheColourOfTheModel(string result, string model, double expected)
    {
        var game = Game(white: "gpt", black: "claude", Day, result: result);

        Assert.Equal(expected, DirectGames.ScoreFor(game, model));
    }

    [Fact]
    public void ScoreForAGameInProgressIsNullNotALoss()
    {
        var game = Game(white: "gpt", black: "claude", Day, state: "running", result: "*");

        Assert.Null(DirectGames.ScoreFor(game, "gpt"));
    }

    private static GameSummaryDto Game(string white, string black, DateTimeOffset createdAt,
        string state = "finished", string result = "1-0") =>
        new(Guid.NewGuid(), null, new PlayerSpec("llm", white), new PlayerSpec("llm", black), true,
            state, result, null, 40, createdAt, state == "finished" ? createdAt.AddMinutes(20) : null);
}
