using Chess.Api.Players;

namespace Chess.Api.Games;

/// <summary>
/// What an awaitable run (<see cref="GameRunner.PlayToEndAsync"/>) adds to an ordinary game (03 §9.1). Ordinary games run
/// without hooks, so their speed and their players are unchanged.
/// </summary>
/// <param name="Players">Players for this game in place of the runner's own, or null to use them.</param>
/// <param name="Pacer">Waits between the moves of this game, or null to play at full speed.</param>
public sealed record GameRunHooks(IChessPlayerFactory? Players = null, IGamePacer? Pacer = null);

/// <summary>Waits between the moves of one game, so that viewers can follow it. Called after every played move.</summary>
public interface IGamePacer
{
    Task AfterMoveAsync(CancellationToken ct);
}
