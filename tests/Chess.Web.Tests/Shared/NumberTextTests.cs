using Chess.Web.Components.Pages.Benchmark;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Pages.Shared;
using Chess.Web.Components.Pages.Tournaments;
using Xunit;

namespace Chess.Web.Tests.Shared;

public class NumberTextTests
{
    [Theory]
    [InlineData(-0.3, "0", "0")]
    [InlineData(-0.004, "0.00", "0.00")]
    [InlineData(-0.04, "0.0", "0.0")]
    [InlineData(-1.4, "0", "-1")]
    [InlineData(-10.2, "0", "-10")]
    [InlineData(-100.0, "0", "-100")]
    [InlineData(0.3, "0", "0")]
    [InlineData(1650.4, "0", "1650")]
    public void ANegativeThatRoundsToZeroIsShownWithoutASign(double value, string format, string expected)
    {
        Assert.Equal(expected, NumberText.Fixed(value, format));
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("-0.00", "0.00")]
    [InlineData("-0 %", "0 %")]
    [InlineData("-0.5", "-0.5")]
    [InlineData("-10", "-10")]
    [InlineData("abc", "abc")]
    [InlineData("-", "-")]
    public void OnlyANegativeZeroLosesItsSign(string text, string expected)
    {
        Assert.Equal(expected, NumberText.WithoutNegativeZero(text));
    }

    [Theory]
    [InlineData(82.4, "+82.4")]
    [InlineData(-40.0, "-40.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(-0.04, "0.0")]
    public void ASignedFigureHasASignUnlessItIsZero(double value, string expected)
    {
        Assert.Equal(expected, NumberText.Signed(value, "0.0"));
    }

    [Fact]
    public void TheTournamentLeaderboardAndTheResultsPageShowNoMinusNought()
    {
        Assert.Equal("0", TournamentFormat.Fixed(-0.4, "0"));
        Assert.Equal("0.00", TournamentFormat.Fixed(-0.001, "0.00"));
        Assert.Equal("0", ResultsModel.Number(-0.4, "0"));
        Assert.Equal("-12", ResultsModel.Number(-12.4, "0"));
        Assert.DoesNotContain('-', TournamentFormat.Percent(-0.0001));
    }

    [Fact]
    public void TheBenchmarkReportKeepsItsSignedEloButNeverMinusNought()
    {
        Assert.Equal("+0", BenchmarkFormat.Signed(-0.3));
        Assert.Equal("−40", BenchmarkFormat.Signed(-40));
        Assert.Equal("+191", BenchmarkFormat.Signed(191.2));
        Assert.Equal("0", BenchmarkFormat.Whole(-0.2));
    }
}
