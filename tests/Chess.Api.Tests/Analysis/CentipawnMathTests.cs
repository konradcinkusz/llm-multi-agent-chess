using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class CentipawnMathTests
{
    [Theory]
    [InlineData(30, -20, 50)]   // the played move gave up 50 cp from the best value
    [InlineData(20, 20, 0)]     // played the best move
    [InlineData(20, 30, 0)]     // better than the engine's best: never negative
    [InlineData(50, -1000, 1000)] // a mate-level blunder is capped at 1000
    [InlineData(1000, -1000, 1000)]
    public void Loss_is_best_minus_played_clamped_to_0_to_1000(int best, int played, int expected)
    {
        Assert.Equal(expected, CentipawnMath.Loss(best, played));
    }
}
