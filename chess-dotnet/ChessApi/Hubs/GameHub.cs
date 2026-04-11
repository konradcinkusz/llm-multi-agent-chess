using ChessApi.DTOs;
using ChessApi.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChessApi.Hubs;

public interface IGameHubClient
{
    Task OnMoveStarted(Guid gameId, string color, string model, int attempt);
    Task OnMoveMade(Guid gameId, MoveResultDto result);
    Task OnGameOver(Guid gameId, string statusText);
    Task OnMoveError(Guid gameId, string message);
    Task OnRetryAttempt(Guid gameId, string color, int attempt, string reason);
}

public class GameHub(IGameService gameService) : Hub<IGameHubClient>
{
    // per-connection auto-play cancellation tokens
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        _autoPlayTokens = new();

    public async Task JoinGame(Guid gameId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());

    public async Task MakeMoveAsync(Guid gameId)
    {
        var session = gameService.GetSession(gameId);
        if (session == null || session.IsGameOver) return;

        string color = session.CurrentTurn;
        string model = color == "White" ? session.WhiteAgent.Model : session.BlackAgent.Model;

        await Clients.Group(gameId.ToString())
            .OnMoveStarted(gameId, color, model, 1);

        async Task OnRetry(int attempt, string reason)
            => await Clients.Group(gameId.ToString())
                .OnRetryAttempt(gameId, color, attempt, reason);

        try
        {
            var result = await session.MakeMoveAsync(OnRetry, Context.ConnectionAborted);
            if (result == null)
            {
                await Clients.Caller.OnMoveError(gameId, "Move failed");
                return;
            }

            await gameService.PersistMoveAsync(gameId, result);

            var dto = DtoMapper.ToDto(result);
            await Clients.Group(gameId.ToString()).OnMoveMade(gameId, dto);

            if (result.IsGameOver)
            {
                await Clients.Group(gameId.ToString()).OnGameOver(gameId, result.StatusText);
                await gameService.FinalizeGameAsync(gameId, result.StatusText);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            await Clients.Caller.OnMoveError(gameId, ex.Message);
        }
    }

    public async Task StartAutoPlay(Guid gameId)
    {
        // Cancel any existing auto-play for this connection
        if (_autoPlayTokens.TryRemove(Context.ConnectionId, out var old)) old.Cancel();

        var cts = new CancellationTokenSource();
        _autoPlayTokens[Context.ConnectionId] = cts;
        var ct = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, Context.ConnectionAborted).Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var session = gameService.GetSession(gameId);
                    if (session == null || session.IsGameOver) break;

                    await MakeMoveAsync(gameId);

                    // Reload session to check game-over after the move
                    session = gameService.GetSession(gameId);
                    if (session == null || session.IsGameOver) break;

                    await Task.Delay(TimeSpan.FromSeconds(1.0), ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { _autoPlayTokens.TryRemove(Context.ConnectionId, out _); }
        }, ct);

        await Task.CompletedTask;
    }

    public Task StopAutoPlay()
    {
        if (_autoPlayTokens.TryRemove(Context.ConnectionId, out var cts)) cts.Cancel();
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (_autoPlayTokens.TryRemove(Context.ConnectionId, out var cts)) cts.Cancel();
        return base.OnDisconnectedAsync(exception);
    }
}
