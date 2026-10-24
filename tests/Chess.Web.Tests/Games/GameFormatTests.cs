using Chess.Web.Components.Pages.Games;
using Xunit;

namespace Chess.Web.Tests.Games;

public class GameFormatTests
{
    [Theory]
    [InlineData(0L, "0 ms")]
    [InlineData(850L, "850 ms")]
    [InlineData(1234L, "1.2 s")]
    public void LatencyIsInMillisecondsUnderASecondAndInSecondsAfter(long milliseconds, string expected)
    {
        Assert.Equal(expected, GameFormat.Latency(milliseconds));
    }

    [Fact]
    public void TokensWithNoneReportedSayNotReported()
    {
        Assert.Equal("not reported", GameFormat.Tokens(null, null));
        Assert.Equal("120 in / 8 out", GameFormat.Tokens(120, 8));
        Assert.Equal("— in / 8 out", GameFormat.Tokens(null, 8));
    }

    [Fact]
    public void AMoveWithoutAnalysisHasNoCentipawnLossAndSaysSo()
    {
        Assert.Equal("not analysed", GameFormat.CentipawnLoss(null));
        Assert.Equal("35 cp", GameFormat.CentipawnLoss(35));
    }

    [Fact]
    public void TerminationNamesReadAsWordsAndAGameWithoutOneShowsADash()
    {
        Assert.Equal("insufficient material", GameFormat.Termination("insufficient-material"));
        Assert.Equal("—", GameFormat.Termination(null));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(45, 3)]
    public void TheArchiveHasEnoughPagesForEveryGameAndAtLeastOne(int total, int expectedPages)
    {
        Assert.Equal(expectedPages, GameArchive.PageCount(total));
    }
}
