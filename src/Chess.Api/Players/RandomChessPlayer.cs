namespace Chess.Api.Players;

/// <summary>
/// The <c>random</c> side: picks one of the legal moves it is given, with a seeded RNG. It never forfeits, and it
/// is the anchor of the rating scale (RATING.md §3). One instance plays one side of one game; it is not thread-safe.
/// </summary>
public sealed class RandomChessPlayer : IChessPlayer
{
    private readonly Random _random;

    public RandomChessPlayer(PlayerDescriptor descriptor, int seed)
    {
        Descriptor = descriptor;
        _random = new Random(seed);
    }

    public PlayerDescriptor Descriptor { get; }

    public Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.LegalSan.Count == 0)
        {
            throw new InvalidOperationException("No legal moves to choose from; the game should already be over.");
        }

        var san = request.LegalSan[_random.Next(request.LegalSan.Count)];
        var attempt = new MoveAttempt(1, string.Empty, san, san, true, TimeSpan.Zero, null, null, null);
        return Task.FromResult(new PlayerMove(san, [attempt], Forfeit: false));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
