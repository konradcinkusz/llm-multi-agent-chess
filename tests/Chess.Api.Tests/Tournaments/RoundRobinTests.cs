using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

public class RoundRobinTests
{
    [Fact]
    public void ThreeModelsTwoGamesEachMakeSixGamesAndEveryPairMeetsTwice()
    {
        var pairings = RoundRobin.Pairings(["a", "b", "c"], 2);

        Assert.Equal(6, pairings.Count);
        foreach (var (first, second) in new[] { ("a", "b"), ("a", "c"), ("b", "c") })
        {
            var meetings = pairings.Where(p => Same(p, first, second)).ToList();
            Assert.Equal(2, meetings.Count);
        }
    }

    [Fact]
    public void EachPairPlaysHalfItsGamesWithEachColour()
    {
        var pairings = RoundRobin.Pairings(["a", "b", "c"], 4);

        foreach (var (first, second) in new[] { ("a", "b"), ("a", "c"), ("b", "c") })
        {
            var meetings = pairings.Where(p => Same(p, first, second)).ToList();
            Assert.Equal(4, meetings.Count);
            Assert.Equal(2, meetings.Count(p => p.White == first));
            Assert.Equal(2, meetings.Count(p => p.White == second));
        }
    }

    [Fact]
    public void EveryModelHasAsManyWhiteGamesAsBlackGames()
    {
        var pairings = RoundRobin.Pairings(["a", "b", "c", "d"], 2);

        foreach (var model in new[] { "a", "b", "c", "d" })
        {
            Assert.Equal(
                pairings.Count(p => p.White == model),
                pairings.Count(p => p.Black == model));
        }
    }

    [Fact]
    public void TwoModelsWithTwoGamesMakeTwoGamesWithOppositeColours()
    {
        var pairings = RoundRobin.Pairings(["a", "b"], 2);

        Assert.Equal([new Pairing("a", "b"), new Pairing("b", "a")], pairings);
    }

    private static bool Same(Pairing pairing, string first, string second) =>
        (pairing.White == first && pairing.Black == second) || (pairing.White == second && pairing.Black == first);
}
