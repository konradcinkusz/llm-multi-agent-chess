using Chess.Api.Games;
using Chess.Api.Players;
using Chess.Engine;

namespace Chess.Api.Broadcast;

/// <summary>
/// A side's move got no reply from the model at all: the gateway failed or timed out on every attempt (03 §9.1). It is a
/// technical failure, not a forfeit, so the game is aborted and the slot is retried instead of being rated.
/// </summary>
public sealed class TechnicalGameFailureException(string message) : Exception(message);

/// <summary>
/// Wraps the players of a broadcast game so that a move with no reply raises <see cref="TechnicalGameFailureException"/>.
/// The game runner records that as aborted, which the rating leaves out, and the runner retries the slot.
/// </summary>
internal sealed class TechnicalFailurePlayers(IChessPlayerFactory inner) : IChessPlayerFactory
{
    public async Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct) =>
        new Watched(await inner.CreateAsync(descriptor, gameId, side, ct));

    private sealed class Watched(IChessPlayer player) : IChessPlayer
    {
        public PlayerDescriptor Descriptor => player.Descriptor;

        public async Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct)
        {
            var move = await player.ChooseMoveAsync(request, ct);
            if (move.Forfeit && move.Attempts.Count > 0 && move.Attempts.All(a => a.RawReply is null))
            {
                throw new TechnicalGameFailureException(
                    $"{Descriptor.ModelId} gave no reply to ply {request.Ply} in game {request.GameId}.");
            }

            return move;
        }

        public ValueTask DisposeAsync() => player.DisposeAsync();
    }
}

/// <summary>Paces a broadcast game: <c>MoveDelayMs</c> after every move, then it holds while the broadcast is paused (03 §9.1).</summary>
internal sealed class BroadcastPacer(TimeSpan moveDelay, BroadcastRunnerSignals signals, TimeProvider clock) : IGamePacer
{
    public async Task AfterMoveAsync(CancellationToken ct)
    {
        await Task.Delay(moveDelay, clock, ct);
        await signals.WaitWhilePausedAsync(ct);
    }
}
