namespace Chess.Api.Broadcast;

/// <summary>Outcome of a broadcast command: T5.3 answers 200, 404 or 409 from it.</summary>
public enum BroadcastCommandResult
{
    /// <summary>The command was applied and the schedule is in its new state.</summary>
    Accepted,

    /// <summary>No schedule has that id.</summary>
    NotFound,

    /// <summary>The schedule is not in a state that the command can leave (03 §9.1).</summary>
    Conflict,
}

/// <summary>
/// Drives the broadcast schedule that is playing (03 §9.1, §9.2). The database holds the state; the controller writes it
/// and signals the runner, which plays the slots. Implemented by the broadcast runner (T5.2).
/// </summary>
public interface IBroadcastController
{
    /// <summary>Draft or Scheduled → Running. The runner plays each slot from its planned start.</summary>
    Task<BroadcastCommandResult> StartAsync(Guid scheduleId, CancellationToken ct);

    /// <summary>Running → Paused. The game in play stops after its current move; the next move waits for resume.</summary>
    Task<BroadcastCommandResult> PauseAsync(Guid scheduleId, CancellationToken ct);

    /// <summary>Paused → Running.</summary>
    Task<BroadcastCommandResult> ResumeAsync(Guid scheduleId, CancellationToken ct);

    /// <summary>Scheduled, Running or Paused → Cancelled. The game in play is aborted.</summary>
    Task<BroadcastCommandResult> StopAsync(Guid scheduleId, CancellationToken ct);

    /// <summary>What the broadcast page and OBS show now: the schedule's status, the game in play and the next slots.</summary>
    Task<NowNextDto> NowAsync(CancellationToken ct);
}
