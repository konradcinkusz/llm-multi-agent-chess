namespace Chess.Api.Broadcast;

/// <summary>
/// The in-process state of the broadcast being played: the pause flag, the game in play and the technical-break notice.
/// The database holds the schedule and its slots; this only lets the controller reach the runner and wakes it.
/// </summary>
public sealed class BroadcastRunnerSignals
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = NewSignal();
    private bool _paused;
    private CancellationTokenSource? _currentGame;
    private string? _notice;

    /// <summary>The notice the broadcast page shows while the runner backs off, or null when all is well.</summary>
    public string? Notice
    {
        get { lock (_gate) return _notice; }
        set { lock (_gate) _notice = value; }
    }

    public bool Paused
    {
        get { lock (_gate) return _paused; }
    }

    /// <summary>Completes at the next <see cref="Publish"/>. A waiter re-reads the state when it completes.</summary>
    public Task Changed
    {
        get { lock (_gate) return _changed.Task; }
    }

    /// <summary>Announces a change of the schedule or of the NowNext view, and wakes everything waiting for one.</summary>
    public void Publish()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _changed;
            _changed = NewSignal();
        }

        previous.TrySetResult();
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
        }

        Publish();
    }

    /// <summary>Holds the caller while the broadcast is paused. Returns at once when it is not.</summary>
    public async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (!_paused)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(ct);
        }
    }

    /// <summary>Registers the abort of the game about to be played, so that a stop reaches it before it starts.</summary>
    public void BeginGame(CancellationTokenSource abort)
    {
        lock (_gate)
        {
            _currentGame = abort;
        }
    }

    public void EndGame(CancellationTokenSource abort)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_currentGame, abort))
            {
                _currentGame = null;
            }
        }
    }

    /// <summary>Aborts the game in play, if any. It is recorded as aborted.</summary>
    public void AbortCurrentGame()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _currentGame;
        }

        current?.Cancel();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
