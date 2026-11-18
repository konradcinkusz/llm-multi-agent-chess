using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class LiveBroadcastViewTests
{
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

    [Theory]
    [InlineData(0, "a moment")]
    [InlineData(-30, "a moment")]
    [InlineData(1, "00:01")]
    [InlineData(59, "00:59")]
    [InlineData(61, "01:01")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(7205, "2:00:05")]
    public void TheCountdownIsMinutesAndSecondsAndHoursFromAnHourOn(int seconds, string expected)
    {
        Assert.Equal(expected, LiveBroadcastView.CountdownText(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ACountdownWithAFractionOfASecondRoundsUpSoItNeverReadsZeroBeforeTheStart()
    {
        Assert.Equal("00:01", LiveBroadcastView.CountdownText(TimeSpan.FromMilliseconds(100)));
        Assert.Equal("00:02", LiveBroadcastView.CountdownText(TimeSpan.FromMilliseconds(1100)));
    }

    [Fact]
    public void AStartTimeIsShownAsHoursAndMinutes()
    {
        var label = LiveBroadcastView.StartLabel(new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero));

        Assert.Matches(@"^\d{2}:\d{2}$", label);
    }
}
