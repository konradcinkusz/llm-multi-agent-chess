using Chess.Api.Benchmarks;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

public class BenchmarkRulesTests
{
    private static readonly ModelListing Listing = new(
    [
        new ModelDto("fake-a", "Fake A", 0, true, null, ["low", "medium", "high"], "medium"),
        new ModelDto("fake-b", "Fake B", 0, true, null, ["low", "medium", "high"], "medium"),
        new ModelDto("old", "Old", 0, false),
        new ModelDto("auto", "Auto", 0, true),
        new ModelDto("plain", "Plain", 1, true),
        new ModelDto("random", "Random", 0, true),
    ], Warning: null);

    [Fact]
    public void TheDefaultOpponentsAreEveryEnabledModelExceptTheCandidatesModelAndAuto()
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a@high", null);

        Assert.Equal(["fake-b", "plain", "random"], BenchmarkRules.OpponentsOf(request, Listing));
    }

    [Fact]
    public void NamedOpponentsReplaceTheDefaults()
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a", ["random"]);

        Assert.Equal(["random"], BenchmarkRules.OpponentsOf(request, Listing));
    }

    [Fact]
    public void AValidRequestHasNoProblems()
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a@high", null, 30, 2);

        Assert.Empty(BenchmarkRules.Validate(request, ["fake-b", "random"], Listing, maxConcurrentGames: 2));
    }

    [Fact]
    public void AnEffortTheModelDoesNotOfferIsAProblemOfTheCandidate()
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a@turbo", null);

        var errors = BenchmarkRules.Validate(request, ["fake-b"], Listing, 2);

        Assert.True(errors.ContainsKey("candidate"));
        Assert.Contains("turbo", errors["candidate"][0]);
    }

    [Fact]
    public void AnEffortOnAModelWithoutEffortsIsRefused()
    {
        var errors = BenchmarkRules.Validate(new CreateBenchmarkRequest("Pick", "plain@low", null), ["fake-b"], Listing, 2);

        Assert.Contains("does not take a reasoning effort", errors["candidate"][0]);
    }

    [Fact]
    public void TheRandomPlayerCannotBeACandidate()
    {
        var errors = BenchmarkRules.Validate(new CreateBenchmarkRequest("Pick", "random", null), ["fake-b"], Listing, 2);

        Assert.True(errors.ContainsKey("candidate"));
    }

    [Fact]
    public void TheCandidateCannotBeItsOwnOpponentAndOpponentsAreUnique()
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a", null);

        Assert.True(BenchmarkRules.Validate(request, ["fake-a"], Listing, 2).ContainsKey("opponents"));
        Assert.True(BenchmarkRules.Validate(request, ["fake-b", "fake-b"], Listing, 2).ContainsKey("opponents"));
        Assert.True(BenchmarkRules.Validate(request, [], Listing, 2).ContainsKey("opponents"));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(0)]
    [InlineData(1)]
    public void GamesPerOpponentMustBeEvenAndAtLeastTwo(int games)
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a", null, games);

        Assert.True(BenchmarkRules.Validate(request, ["fake-b"], Listing, 2).ContainsKey("gamesPerOpponent"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MaxConcurrencyIsBoundedByTheGameCap(int concurrency)
    {
        var request = new CreateBenchmarkRequest("Pick", "fake-a", null, 4, concurrency);

        Assert.True(BenchmarkRules.Validate(request, ["fake-b"], Listing, maxConcurrentGames: 2).ContainsKey("maxConcurrency"));
    }
}
