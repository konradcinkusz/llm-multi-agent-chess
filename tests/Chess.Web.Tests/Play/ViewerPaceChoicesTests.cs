using Chess.Web.Components.Pages.Play;
using Xunit;

namespace Chess.Web.Tests.Play;

public class ViewerPaceChoicesTests
{
    [Fact]
    public void TheConfiguredPaceIsUsedWhenItIsOneOfTheOffered()
    {
        Assert.Equal(TimeSpan.Zero, PlayChoices.DefaultPace("0"));
        Assert.Equal(TimeSpan.FromSeconds(5), PlayChoices.DefaultPace("5"));
    }

    [Fact]
    public void TwoSecondsIsTheDefaultWhenNothingOrAnUnofferedPaceIsConfigured()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), PlayChoices.DefaultPace(null));
        Assert.Equal(TimeSpan.FromSeconds(2), PlayChoices.DefaultPace("4"));
        Assert.Equal(TimeSpan.FromSeconds(2), PlayChoices.DefaultPace("soon"));
    }

    [Theory]
    [InlineData(1, "1 move behind the game")]
    [InlineData(3, "3 moves behind the game")]
    public void TheBehindCountIsWordedForOneMoveAndForMore(int moves, string text)
    {
        Assert.Equal(text, PlayChoices.Behind(moves));
    }
}
