using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class LiveBroadcastViewTests
{
    [Theory]
    [InlineData(null, 8)]
    [InlineData(0, 8)]
    [InlineData(-3, 8)]
    [InlineData(3, 3)]
    [InlineData(50, 20)]
    public void TopIsTheDefaultWhenAbsentOrNotPositiveAndCappedAtTwenty(int? top, int expected)
    {
        Assert.Equal(expected, LiveBroadcastView.ClampTop(top));
    }

    [Fact]
    public void TheLeaderboardIsOrderedByArenaScoreThenRatingThenIdAndCutToTop()
    {
        var models = new[]
        {
            Rating("zeta", arena: 0.50, rating: 1500),
            Rating("beta", arena: 0.70, rating: 1400),
            Rating("alpha", arena: 0.50, rating: 1600),
            Rating("gamma", arena: 0.50, rating: 1500),
        };

        var board = LiveBroadcastView.Leaderboard(models, 3);

        Assert.Equal(new[] { "beta", "alpha", "gamma" }, board.Select(model => model.Model).ToArray());
    }

    [Fact]
    public void TheTitleNamesTheGameOfTheScheduleWhileOneIsOnAndTheChannelOtherwise()
    {
        Assert.Equal("LLM Chess 24h", LiveBroadcastView.Title(null, 48));
        Assert.Equal("LLM Chess 24h — Game 17 of 48", LiveBroadcastView.Title(16, 48));
        Assert.Equal("LLM Chess 24h — Game 1", LiveBroadcastView.Title(0, null));
    }

    [Fact]
    public void AProvisionalModelSortsAfterEstablishedOnesEvenWithAHigherArenaScore()
    {
        var models = new[]
        {
            Rating("new", arena: 0.90, rating: 0) with { Provisional = true },
            Rating("old", arena: 0.40, rating: 1400),
        };

        var board = LiveBroadcastView.Leaderboard(models, 8);

        Assert.Equal(new[] { "old", "new" }, board.Select(model => model.Model).ToArray());
    }

    [Fact]
    public void TheMoveTailKeepsTheLastSixMovesAndLeavesBlackEmptyWhileBlackIsToMove()
    {
        var moves = Enumerable.Range(1, 15)
            // The server writes colours in lower case; the tail must not depend on them.
            .Select(ply => new MoveDto(ply, ply % 2 == 1 ? "white" : "black", $"M{ply}", "fen", 1, 0, null, null, null, null))
            .ToList();

        var tail = LiveBroadcastView.MoveTail(moves);

        Assert.Equal(6, tail.Count);
        Assert.Equal(3, tail[0].Number);
        Assert.Equal(new MoveRow(8, "M15", null), tail[^1]);
        Assert.Equal(new MoveRow(7, "M13", "M14"), tail[^2]);
    }

    [Fact]
    public void AModelKeepsItsColourAndTheLightAndDarkPalettesDiffer()
    {
        var sorted = new[] { "claude", "gemini", "gpt" };

        Assert.Equal(LiveBroadcastView.DarkSeries[1], LiveBroadcastView.Colour("gemini", sorted, light: false));
        Assert.Equal(LiveBroadcastView.Colour("gpt", sorted, light: false), LiveBroadcastView.Colour("gpt", sorted, light: false));
        Assert.NotEqual(LiveBroadcastView.Colour("gpt", sorted, light: false), LiveBroadcastView.Colour("gpt", sorted, light: true));
    }

    [Fact]
    public void AStartTimeIsShownAsHoursAndMinutes()
    {
        var label = LiveBroadcastView.StartLabel(new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero));

        Assert.Matches(@"^\d{2}:\d{2}$", label);
    }

    private static ModelRating Rating(string model, double arena, double rating) =>
        new(model, rating, rating - 50, rating + 50, 10, 0.5, 1, null, 0.5, arena, false);
}
