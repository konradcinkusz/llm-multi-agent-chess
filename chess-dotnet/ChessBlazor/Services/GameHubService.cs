using ChessBlazor.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessBlazor.Services;

public class GameHubService : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Func<Guid, string, string, int, Task>? OnMoveStarted;
    public event Func<Guid, MoveResultMessage, Task>? OnMoveMade;
    public event Func<Guid, string, Task>? OnGameOver;
    public event Func<Guid, string, Task>? OnMoveError;
    public event Func<Guid, string, int, string, Task>? OnRetryAttempt;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task ConnectAsync(string hubUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid, string, string, int>("OnMoveStarted",
            (id, color, model, attempt) =>
                OnMoveStarted?.Invoke(id, color, model, attempt) ?? Task.CompletedTask);

        _connection.On<Guid, MoveResultMessage>("OnMoveMade",
            (id, result) => OnMoveMade?.Invoke(id, result) ?? Task.CompletedTask);

        _connection.On<Guid, string>("OnGameOver",
            (id, status) => OnGameOver?.Invoke(id, status) ?? Task.CompletedTask);

        _connection.On<Guid, string>("OnMoveError",
            (id, msg) => OnMoveError?.Invoke(id, msg) ?? Task.CompletedTask);

        _connection.On<Guid, string, int, string>("OnRetryAttempt",
            (id, color, attempt, reason) =>
                OnRetryAttempt?.Invoke(id, color, attempt, reason) ?? Task.CompletedTask);

        await _connection.StartAsync();
    }

    public Task JoinGameAsync(Guid gameId)
        => _connection!.InvokeAsync("JoinGame", gameId);

    public Task MakeMoveAsync(Guid gameId)
        => _connection!.InvokeAsync("MakeMoveAsync", gameId);

    public Task StartAutoPlayAsync(Guid gameId)
        => _connection!.InvokeAsync("StartAutoPlay", gameId);

    public Task StopAutoPlayAsync()
        => _connection!.InvokeAsync("StopAutoPlay");

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}
