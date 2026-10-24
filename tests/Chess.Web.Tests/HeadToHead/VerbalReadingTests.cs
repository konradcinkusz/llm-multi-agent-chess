using Chess.Web.Api;
using Chess.Web.Components.Pages.HeadToHead;
using Chess.Web.Components.Pages.Shared;
using Xunit;

namespace Chess.Web.Tests.HeadToHead;

/// <summary>The bands of RATING.md §5: 0.50–0.55, 0.55–0.65, 0.65–0.80, above 0.80, and significance from the interval.</summary>
public class VerbalReadingTests
{
    [Theory]
    [InlineData(0.50, "indistinguishable")]
    [InlineData(0.54, "indistinguishable")]
    [InlineData(0.55, "slightly better")]
    [InlineData(0.64, "slightly better")]
    [InlineData(0.65, "clearly better")]
    [InlineData(0.80, "clearly better")]
    [InlineData(0.81, "dominant")]
    [InlineData(0.95, "dominant")]
    public void StrengthFollowsTheBandsOfTheRatingGuide(double rqc, string expected)
    {
        Assert.Equal(expected, VerbalReading.Strength(rqc));
    }

    [Theory]
    [InlineData(0.30)]
    [InlineData(0.45)]
    [InlineData(0.62)]
    [InlineData(0.10)]
    public void StrengthIsTheSameForBothSidesOfTheCoefficient(double rqc)
    {
        // RQC(A,B) + RQC(B,A) = 1, so reading the strength from the larger side must not depend on which side is A.
        Assert.Equal(VerbalReading.Strength(rqc), VerbalReading.Strength(1 - rqc));
    }

    [Fact]
    public void IntervalContainingHalfIsNotSignificantWhateverThePointValue()
    {
        Assert.False(Significance.IsSignificant(Pair("gpt", "claude", rqc: 0.7, low: 0.45, high: 0.95), bothEstablished: true));
        Assert.False(Significance.IsSignificant(Pair("gpt", "claude", rqc: 0.7, low: 0.5, high: 0.9), bothEstablished: true));
        Assert.True(Significance.IsSignificant(Pair("gpt", "claude", rqc: 0.7, low: 0.51, high: 0.9), bothEstablished: true));
        Assert.True(Significance.IsSignificant(Pair("gpt", "claude", rqc: 0.3, low: 0.1, high: 0.49), bothEstablished: true));
    }

    [Fact]
    public void SentenceNamesTheLeaderAndTheTrailer()
    {
        var pair = Pair("gpt", "claude", rqc: 0.62, low: 0.55, high: 0.69);

        Assert.Equal("gpt is slightly better than claude.", VerbalReading.Sentence(pair, bothEstablished: true));
    }

    [Fact]
    public void SentenceNamesBModelWhenItIsTheLeader()
    {
        var pair = Pair("gpt", "claude", rqc: 0.1, low: 0.05, high: 0.15);

        Assert.Equal("claude is dominant over gpt.", VerbalReading.Sentence(pair, bothEstablished: true));
    }

    [Fact]
    public void DominantReadsAsDominantOver()
    {
        var pair = Pair("gpt", "claude", rqc: 0.9, low: 0.85, high: 0.95);

        Assert.Equal("gpt is dominant over claude.", VerbalReading.Sentence(pair, bothEstablished: true));
    }

    [Fact]
    public void SentenceSaysWhenTheModelsAreIndistinguishable()
    {
        var pair = Pair("gpt", "claude", rqc: 0.5, low: 0.4, high: 0.6);

        Assert.Equal("gpt and claude are indistinguishable. Not significant: the 95 % interval contains 0.5.",
            VerbalReading.Sentence(pair, bothEstablished: true));
    }

    [Fact]
    public void SentenceAddsTheNotSignificantNoteWhenTheIntervalContainsHalf()
    {
        var pair = Pair("gpt", "claude", rqc: 0.7, low: 0.45, high: 0.85);

        Assert.EndsWith("Not significant: the 95 % interval contains 0.5.", VerbalReading.Sentence(pair, bothEstablished: true), StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleGameIsNotSignificantEvenWithAPointIntervalAwayFromHalf()
    {
        // One game: the bootstrap interval collapses to the point estimate, so it must not read as a lead.
        var pair = Pair("gpt", "claude", rqc: 0.7, low: 0.7, high: 0.7);

        var sentence = VerbalReading.Sentence(pair, bothEstablished: true);

        Assert.False(Significance.IsSignificant(pair, bothEstablished: true));
        Assert.EndsWith("Not significant: the 95 % interval has no width.", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AProvisionalModelIsNotSignificantEvenWithAWideSeparation()
    {
        var pair = Pair("gpt", "claude", rqc: 0.9, low: 0.85, high: 0.95);

        var sentence = VerbalReading.Sentence(pair, bothEstablished: false);

        Assert.False(Significance.IsSignificant(pair, bothEstablished: false));
        Assert.Equal("gpt is dominant over claude. Not yet significant: too few games (need 20 rated games per model).", sentence);
    }

    [Fact]
    public void AnEstablishedLeadWithAnIntervalAboveHalfIsSignificant()
    {
        var pair = Pair("gpt", "claude", rqc: 0.62, low: 0.55, high: 0.70);

        Assert.True(Significance.IsSignificant(pair, bothEstablished: true));
        Assert.Equal("gpt is slightly better than claude.", VerbalReading.Sentence(pair, bothEstablished: true));
    }

    private static PairwiseCoefficient Pair(string a, string b, double rqc, double low, double high) =>
        new(a, b, ExpectedScore: 0.5, QualityShare: 0.5, Rqc: rqc, RqcLow: low, RqcHigh: high, EloEquivalent: 0,
            HeadToHeadGames: 0, HeadToHeadScore: null);
}
