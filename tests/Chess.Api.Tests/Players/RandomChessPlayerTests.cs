using Chess.Api.Players;
using Xunit;

namespace Chess.Api.Tests.Players;

public class RandomChessPlayerTests
{
    private static readonly PlayerDescriptor Descriptor = new("random", "random");

    [Fact]
    public async Task PicksALegalMoveInOneAttempt()
    {
        var player = new RandomChessPlayer(Descriptor, seed: 1);
        var request = PlayerTestData.StartRequest();

        var move = await player.ChooseMoveAsync(request, CancellationToken.None);

        Assert.False(move.Forfeit);
        Assert.Contains(move.San, request.LegalSan);
        var attempt = Assert.Single(move.Attempts);
        Assert.True(attempt.Legal);
        Assert.Equal(move.San, attempt.ParsedSan);
    }

    [Fact]
    public async Task SameSeedGivesTheSameSequenceOfMoves()
    {
        var request = PlayerTestData.StartRequest();
        var first = new RandomChessPlayer(Descriptor, seed: 42);
        var second = new RandomChessPlayer(Descriptor, seed: 42);

        var firstMoves = new List<string?>();
        var secondMoves = new List<string?>();
        for (var i = 0; i < 5; i++)
        {
            firstMoves.Add((await first.ChooseMoveAsync(request, CancellationToken.None)).San);
            secondMoves.Add((await second.ChooseMoveAsync(request, CancellationToken.None)).San);
        }

        Assert.Equal(firstMoves, secondMoves);
    }

    [Fact]
    public async Task NoLegalMovesIsAProgrammingErrorNotAForfeit()
    {
        var player = new RandomChessPlayer(Descriptor, seed: 1);
        var request = PlayerTestData.RequestFor(PlayerTestData.StartFen, []) with { LegalSan = [] };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => player.ChooseMoveAsync(request, CancellationToken.None));
    }
}
