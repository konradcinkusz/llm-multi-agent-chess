using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Broadcast;

/// <summary>The <c>/now</c> status values (03 §9.2): the schedule's own status while one plays, or <c>idle</c>.</summary>
public static class NowNextStatus
{
    public const string Idle = "idle";
}

/// <summary>
/// Writes the schedule's state for the runner and answers <c>now</c> (03 §9.1). Each command is one database write followed by
/// a signal to the runner. Commands are serialised, so two of them cannot both see the same state.
/// </summary>
public sealed class BroadcastRunnerController(
    IDbContextFactory<ArenaDbContext> factory,
    BroadcastRunnerSignals signals,
    TimeProvider clock) : IBroadcastController
{
    /// <summary>How many upcoming slots <c>now</c> lists (the "Up next" panel).</summary>
    private const int UpNextCount = 3;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<BroadcastCommandResult> StartAsync(Guid scheduleId, CancellationToken ct) => ChangeAsync(scheduleId, ct, async (db, schedule) =>
    {
        if (schedule.Status is not (ScheduleStatus.Draft or ScheduleStatus.Scheduled))
        {
            return BroadcastCommandResult.Conflict;
        }

        // One channel: a second schedule starts only after the playing one has finished, been stopped or is paused for good.
        if (await db.BroadcastSchedules.AnyAsync(s => s.Id != schedule.Id
                && (s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused), ct))
        {
            return BroadcastCommandResult.Conflict;
        }

        schedule.Status = ScheduleStatus.Running;
        schedule.StartedAt ??= clock.GetUtcNow();
        signals.SetPaused(false);
        return BroadcastCommandResult.Accepted;
    });

    public Task<BroadcastCommandResult> PauseAsync(Guid scheduleId, CancellationToken ct) => ChangeAsync(scheduleId, ct, (_, schedule) =>
    {
        if (schedule.Status != ScheduleStatus.Running)
        {
            return Task.FromResult(BroadcastCommandResult.Conflict);
        }

        schedule.Status = ScheduleStatus.Paused;
        signals.SetPaused(true);
        return Task.FromResult(BroadcastCommandResult.Accepted);
    });

    public Task<BroadcastCommandResult> ResumeAsync(Guid scheduleId, CancellationToken ct) => ChangeAsync(scheduleId, ct, (_, schedule) =>
    {
        if (schedule.Status != ScheduleStatus.Paused)
        {
            return Task.FromResult(BroadcastCommandResult.Conflict);
        }

        schedule.Status = ScheduleStatus.Running;
        signals.SetPaused(false);
        return Task.FromResult(BroadcastCommandResult.Accepted);
    });

    public Task<BroadcastCommandResult> StopAsync(Guid scheduleId, CancellationToken ct) => ChangeAsync(scheduleId, ct, (_, schedule) =>
    {
        if (schedule.Status is not (ScheduleStatus.Scheduled or ScheduleStatus.Running or ScheduleStatus.Paused))
        {
            return Task.FromResult(BroadcastCommandResult.Conflict);
        }

        schedule.Status = ScheduleStatus.Cancelled;
        schedule.FinishedAt = clock.GetUtcNow();
        signals.Notice = null;
        signals.SetPaused(false);
        // The game in play is aborted; the runner records its slot as skipped.
        signals.AbortCurrentGame();
        return Task.FromResult(BroadcastCommandResult.Accepted);
    });

    public async Task<NowNextDto> NowAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = await db.BroadcastSchedules
            .Where(s => s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused)
            .OrderBy(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (active is null)
        {
            return new NowNextDto(NowNextStatus.Idle, null, [], null, null);
        }

        var current = await db.BroadcastSlots
            .Where(s => s.ScheduleId == active.Id && s.State == SlotState.Running)
            .OrderBy(s => s.Index)
            .FirstOrDefaultAsync(ct);
        var next = await db.BroadcastSlots
            .Where(s => s.ScheduleId == active.Id && s.State == SlotState.Pending)
            .OrderBy(s => s.Index)
            .Take(UpNextCount)
            .ToListAsync(ct);

        return new NowNextDto(
            active.Status,
            current is null ? null : ToDto(current),
            next.Select(ToDto).ToList(),
            signals.Notice,
            active.EndsAt);
    }

    /// <summary>Runs one command under the gate: loads the schedule, applies the change, and saves it when it was accepted.</summary>
    private async Task<BroadcastCommandResult> ChangeAsync(
        Guid scheduleId,
        CancellationToken ct,
        Func<ArenaDbContext, BroadcastScheduleEntity, Task<BroadcastCommandResult>> apply)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
            if (schedule is null)
            {
                return BroadcastCommandResult.NotFound;
            }

            var result = await apply(db, schedule);
            if (result == BroadcastCommandResult.Accepted)
            {
                await db.SaveChangesAsync(ct);
                signals.Publish();
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SlotDto ToDto(BroadcastSlotEntity slot) => new(
        slot.Id,
        slot.Index,
        new PlayerSpec(slot.WhiteKind, slot.WhiteModel),
        new PlayerSpec(slot.BlackKind, slot.BlackModel),
        slot.PlannedStart,
        slot.PlannedEnd,
        slot.State,
        slot.GameId);
}
