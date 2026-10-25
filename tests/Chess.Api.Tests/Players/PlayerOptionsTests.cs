using Chess.Api.Players;
using Xunit;

namespace Chess.Api.Tests.Players;

public class PlayerOptionsTests
{
    [Fact]
    public void TheMoveTimeoutDefaultsToThreeMinutes()
    {
        // Reasoning models routinely think longer than a minute (03 §10.3).
        Assert.Equal(180, new PlayerOptions().MoveTimeoutSeconds);
    }
}
