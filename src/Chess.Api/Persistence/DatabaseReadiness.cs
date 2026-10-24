namespace Chess.Api.Persistence;

public enum DatabaseState { Migrating, Ready, Failed }

public sealed record DatabaseStatus(DatabaseState State, string? Error, int SweptGames);

/// <summary>
/// Outcome of the startup migration. The health check reads <see cref="Current"/>; tests and
/// anything else that must wait for the schema await <see cref="Finished"/>.
/// </summary>
public sealed class DatabaseReadiness
{
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile DatabaseStatus _current = new(DatabaseState.Migrating, null, 0);

    public DatabaseStatus Current => _current;

    /// <summary>Completes once the state has left <see cref="DatabaseState.Migrating"/>, successfully or not.</summary>
    public Task Finished => _finished.Task;

    public void MarkReady(int sweptGames)
    {
        _current = new DatabaseStatus(DatabaseState.Ready, null, sweptGames);
        _finished.TrySetResult();
    }

    public void MarkFailed(string error)
    {
        _current = new DatabaseStatus(DatabaseState.Failed, error, 0);
        _finished.TrySetResult();
    }
}
