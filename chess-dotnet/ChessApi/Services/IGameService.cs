using ChessApi.DTOs;
using ChessApi.Sessions;

namespace ChessApi.Services;

public interface IGameService
{
    Task<GameResponse> CreateGameAsync(CreateGameRequest request);
    GameResponse? GetGame(Guid id);
    IEnumerable<GameSummary> ListGames();
    bool AbandonGame(Guid id);
    ChessGameSession? GetSession(Guid id);
    Task PersistMoveAsync(Guid gameId, MoveResult result);
    Task FinalizeGameAsync(Guid gameId, string status);
}
