namespace Chess.Api.Broadcast;

// Stub from T5.2 (early merge for T5.3). The full controller replaces it in the same task.
public sealed class BroadcastRunnerController : IBroadcastController
{
    public Task<BroadcastCommandResult> StartAsync(Guid scheduleId, CancellationToken ct) => throw new NotImplementedException();

    public Task<BroadcastCommandResult> PauseAsync(Guid scheduleId, CancellationToken ct) => throw new NotImplementedException();

    public Task<BroadcastCommandResult> ResumeAsync(Guid scheduleId, CancellationToken ct) => throw new NotImplementedException();

    public Task<BroadcastCommandResult> StopAsync(Guid scheduleId, CancellationToken ct) => throw new NotImplementedException();

    public Task<NowNextDto> NowAsync(CancellationToken ct) => throw new NotImplementedException();
}
