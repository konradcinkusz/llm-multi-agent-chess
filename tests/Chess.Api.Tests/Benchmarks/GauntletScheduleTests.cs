using Chess.Api.Benchmarks;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Benchmarks;

public class GauntletScheduleTests
{
    [Fact]
    public void EachOpponentGetsItsGamesAndTheCandidateHasHalfWithWhite()
    {
        var pairings = GauntletSchedule.Pairings("claude@medium", ["fake-b", "random"], 4);

        Assert.Equal(8, pairings.Count);
        foreach (var opponent in new[] { "fake-b", "random" })
        {
            var games = pairings.Where(p => p.White == opponent || p.Black == opponent).ToList();
            Assert.Equal(4, games.Count);
            Assert.Equal(2, games.Count(p => p.White == "claude@medium"));
            Assert.Equal(2, games.Count(p => p.Black == "claude@medium"));
        }
    }

    [Fact]
    public void TheGamesFollowTheOrderOfTheOpponents()
    {
        var pairings = GauntletSchedule.Pairings("c", ["a", "b"], 2);

        Assert.Equal([new Pairing("c", "a"), new Pairing("a", "c"), new Pairing("c", "b"), new Pairing("b", "c")], pairings);
    }

    [Fact]
    public void ThirtyGamesPerOpponentGiveFifteenWithEachColour()
    {
        var pairings = GauntletSchedule.Pairings("c", ["a"], 30);

        Assert.Equal(15, pairings.Count(p => p.White == "c"));
        Assert.Equal(15, pairings.Count(p => p.Black == "c"));
    }
}
