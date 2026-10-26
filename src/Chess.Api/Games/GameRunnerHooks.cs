using Chess.Api.Players;

namespace Chess.Api.Games;

/// <summary>
/// What an awaitable run (<see cref="GameRunner.PlayToEndAsync"/>) adds to an ordinary game (03 §9.1, §15.2). Ordinary games
/// run without hooks, so their speed, their players and their end are unchanged.
/// </summary>
/// <param name="Players">Players for this game in place of the runner's own, or null to use them.</param>
/// <param name="Pacer">Waits between the moves of this game, or null to play at full speed.</param>
/// <param name="Budget">Ends this game when its running cost reaches its budget (03 §15.2), or null for no budget.</param>
public sealed record GameRunHooks(IChessPlayerFactory? Players = null, IGamePacer? Pacer = null, IGameBudget? Budget = null);

/// <summary>Waits between the moves of one game, so that viewers can follow it. Called after every played move.</summary>
public interface IGamePacer
{
    Task AfterMoveAsync(CancellationToken ct);
}

/// <summary>
/// Says whether a game has spent its budget. Asked after every played move, so a game stops within one move of its budget.
/// </summary>
public interface IGameBudget
{
    Task<bool> IsExhaustedAsync(Guid gameId, CancellationToken ct);
}

/// <summary>
/// Raised out of the pacing step when the budget is spent. The runner then ends the game with <see cref="GameTermination.BudgetReached"/>
/// at the position on the board, the same way it adjudicates at <c>MaxPlies</c>.
/// </summary>
public sealed class GameBudgetExhaustedException(Guid gameId) : Exception($"Game {gameId} reached its budget.");
