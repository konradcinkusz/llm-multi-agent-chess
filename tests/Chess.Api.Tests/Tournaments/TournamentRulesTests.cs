using Chess.Api.Contracts;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

public class TournamentRulesTests
{
    [Fact]
    public void AValidRequestHasNoProblems()
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("Spring", ["a", "b", "c"], 2, 2), maxConcurrentGames: 2);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0)]
    public void GamesPerPairMustBeAnEvenNumberAtLeastTwo(int gamesPerPair)
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("Spring", ["a", "b"], gamesPerPair, 1), maxConcurrentGames: 2);

        Assert.Contains("gamesPerPair", errors.Keys);
    }

    [Fact]
    public void ARoundRobinNeedsAtLeastTwoModels()
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("Spring", ["a"], 2, 1), maxConcurrentGames: 2);

        Assert.Contains("models", errors.Keys);
    }

    [Fact]
    public void AModelMayAppearOnlyOnce()
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("Spring", ["a", "a"], 2, 1), maxConcurrentGames: 2);

        Assert.Contains("models", errors.Keys);
    }

    [Fact]
    public void ATournamentNeedsAName()
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("  ", ["a", "b"], 2, 1), maxConcurrentGames: 2);

        Assert.Contains("name", errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MaxConcurrencyIsBetweenOneAndTheRunnerLimit(int maxConcurrency)
    {
        var errors = TournamentRules.Validate(new CreateTournamentRequest("Spring", ["a", "b"], 2, maxConcurrency), maxConcurrentGames: 2);

        Assert.Contains("maxConcurrency", errors.Keys);
    }
}
