using Chess.Rating;
using Xunit;

namespace Chess.Rating.Tests;

public class RatingOptionsTests
{
    [Fact]
    public void DefaultsMatchTheFrozenContract()
    {
        var options = new RatingOptions();

        Assert.Equal("random", options.AnchorModel);
        Assert.Equal(0, options.AnchorRating);
        Assert.Equal(350, options.PriorSigma);
        Assert.Equal(200, options.BootstrapSamples);
        Assert.Equal(20261024, options.Seed);
        Assert.Equal(20, options.ProvisionalBelowGames);
        Assert.Equal(0.75, options.ResultWeight);
        Assert.Equal(0.6, options.MoveQualityWeight);
    }
}
