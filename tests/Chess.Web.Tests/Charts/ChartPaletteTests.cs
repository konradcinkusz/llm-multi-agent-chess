using Chess.Web.Components.Charts;
using Xunit;

namespace Chess.Web.Tests.Charts;

public class ChartPaletteTests
{
    [Fact]
    public void SeriesSlotsResolveToTheirCustomProperty()
    {
        Assert.Equal("var(--chart-series-0)", ChartPalette.Series(0));
        Assert.Equal("var(--chart-series-7)", ChartPalette.Series(7));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void SlotsOutsideTheEightFixedOnesAreRefused(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartPalette.Series(slot));
    }

    [Fact]
    public void SlotsFollowTheAlphabeticalOrderOfTheKeys()
    {
        var slots = ChartPalette.AssignSlots(["gpt-x", "claude-y", "gemini-z"]);

        Assert.Equal(0, slots["claude-y"]);
        Assert.Equal(1, slots["gemini-z"]);
        Assert.Equal(2, slots["gpt-x"]);
    }

    [Fact]
    public void ANinthModelGetsNoSlotSoItCanFoldIntoOther()
    {
        var keys = Enumerable.Range(1, 10).Select(i => $"model-{i:00}").Reverse().ToList();

        var slots = ChartPalette.AssignSlots(keys);

        Assert.Equal(ChartPalette.SeriesSlots, slots.Count);
        Assert.Equal(7, slots["model-08"]);
        Assert.False(slots.ContainsKey("model-09"));
        Assert.False(slots.ContainsKey("model-10"));
    }

    [Fact]
    public void DuplicateKeysDoNotTakeASecondSlot()
    {
        var slots = ChartPalette.AssignSlots(["a", "b", "a"]);

        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots["b"]);
    }
}
