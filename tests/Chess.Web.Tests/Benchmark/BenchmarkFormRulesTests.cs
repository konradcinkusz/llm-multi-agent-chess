using Chess.Web.Components.Pages.Benchmark;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkFormRulesTests
{
    [Fact]
    public void AValidFormHasNoProblems()
    {
        Assert.Empty(BenchmarkFormRules.Problems("Spring check", "fake-a@high", 3, 30, 2));
    }

    [Fact]
    public void EachProblemIsReportedInTheOrderTheFormShowsIt()
    {
        var problems = BenchmarkFormRules.Problems("  ", null, 0, 3, 5);

        Assert.Equal(
            new[]
            {
                "Give the benchmark a name.",
                "Choose a candidate model.",
                "Choose at least one opponent.",
                "Games per opponent must be an even number, at least 2, so that colours alternate.",
                "Max concurrency must be between 1 and 2.",
            },
            problems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void GamesPerOpponentMustBeEvenAndAtLeastTwo(int games)
    {
        Assert.Contains(BenchmarkFormRules.Problems("x", "fake-a", 1, games, 1), problem => problem.StartsWith("Games per opponent"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ConcurrencyIsBoundedByTheFormsLimit(int concurrency)
    {
        Assert.Contains(BenchmarkFormRules.Problems("x", "fake-a", 1, 2, concurrency), problem => problem.StartsWith("Max concurrency"));
    }
}
