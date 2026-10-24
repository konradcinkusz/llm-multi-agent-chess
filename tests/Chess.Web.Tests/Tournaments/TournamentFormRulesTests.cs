using Chess.Web.Components.Pages.Tournaments;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class TournamentFormRulesTests
{
    [Fact]
    public void AValidFormHasNoProblems()
    {
        Assert.Empty(TournamentFormRules.Problems("Spring", ["gpt", "claude"], 2, 2));
    }

    [Theory]
    [InlineData("", "gpt,claude", 2, 2, "Give the tournament a name.")]
    [InlineData("Spring", "gpt", 2, 2, "Choose at least two models.")]
    [InlineData("Spring", "gpt,claude", 3, 2, "Games per pair must be an even number, at least 2, so that colours alternate.")]
    [InlineData("Spring", "gpt,claude", 0, 2, "Games per pair must be an even number, at least 2, so that colours alternate.")]
    [InlineData("Spring", "gpt,claude", 2, 0, "Max concurrency must be between 1 and 2.")]
    [InlineData("Spring", "gpt,claude", 2, 3, "Max concurrency must be between 1 and 2.")]
    public void EachBrokenRuleIsReportedOnce(string name, string models, int gamesPerPair, int maxConcurrency, string problem)
    {
        var selected = models.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var problems = TournamentFormRules.Problems(name, selected, gamesPerPair, maxConcurrency);

        Assert.Equal([problem], problems);
    }

    [Fact]
    public void ABlankNameIsAProblemEvenWithSpaces()
    {
        Assert.Contains("Give the tournament a name.", TournamentFormRules.Problems("   ", ["gpt", "claude"], 2, 2));
    }
}
