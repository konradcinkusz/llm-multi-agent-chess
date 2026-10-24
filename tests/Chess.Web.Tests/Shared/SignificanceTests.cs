using Chess.Web.Api;
using Chess.Web.Components.Pages.Shared;
using Xunit;

namespace Chess.Web.Tests.Shared;

/// <summary>The one significance rule of RATING.md §5 that both the Results and the head-to-head pages apply.</summary>
public class SignificanceTests
{
    [Fact]
    public void AnIntervalJustInsideTheToleranceIsNotAClearLead()
    {
        Assert.False(Significance.IntervalShowsLead(0.5 + 1e-7, 0.9));
        Assert.False(Significance.IntervalShowsLead(0.1, 0.5 - 1e-7));
    }

    [Fact]
    public void AnIntervalJustOutsideTheToleranceIsAClearLead()
    {
        Assert.True(Significance.IntervalShowsLead(0.5 + 1e-5, 0.9));
        Assert.True(Significance.IntervalShowsLead(0.1, 0.5 - 1e-5));
    }

    [Fact]
    public void APointIntervalIsNeverAClearLead()
    {
        Assert.False(Significance.IntervalShowsLead(0.7, 0.7));
        Assert.False(Significance.IntervalShowsLead(0.7, 0.7 + 1e-7));
    }

    [Fact]
    public void AProvisionalModelMakesAResultNotSignificantWhateverTheInterval()
    {
        var pair = Pair(rqc: 0.95, low: 0.9, high: 0.98);

        Assert.True(Significance.IsSignificant(pair, bothEstablished: true));
        Assert.False(Significance.IsSignificant(pair, bothEstablished: false));
    }

    [Fact]
    public void BothModelsMustBeEstablishedAndPresent()
    {
        var ratings = new[]
        {
            Rating("alpha", provisional: false),
            Rating("beta", provisional: false),
            Rating("gamma", provisional: true),
        };

        Assert.True(Significance.BothEstablished(ratings, "alpha", "beta"));
        Assert.False(Significance.BothEstablished(ratings, "alpha", "gamma"));
        Assert.False(Significance.BothEstablished(ratings, "alpha", "delta"));
    }

    private static PairwiseCoefficient Pair(double rqc, double low, double high) =>
        new("alpha", "beta", ExpectedScore: 0.5, QualityShare: 0.5, Rqc: rqc, RqcLow: low, RqcHigh: high, EloEquivalent: 0,
            HeadToHeadGames: 0, HeadToHeadScore: null);

    private static ModelRating Rating(string model, bool provisional) =>
        new(model, 100, 80, 120, provisional ? 5 : 30, 0.5, 1, null, 1, 50, provisional);
}
