using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chess.Api.Broadcast;

/// <summary>
/// Plays the slots of the running broadcast schedule one game at a time, paced for viewers (03 §9.1, ADR-011). It waits for
/// each slot's planned start, creates the game as a rated game, plays it with the schedule's move delay, then waits the game
/// gap. A game that gets no reply from the model is retried after a back-off; after <see cref="MaxAttempts"/> the slot is
/// skipped and the schedule moves on. Nothing that happens to one game ends the stream, and nothing escapes
/// <see cref="ExecuteAsync"/>.
/// </summary>
public sealed class BroadcastRunner(
    IDbContextFactory<ArenaDbContext> factory,
    GameRunner games,
    IChessPlayerFactory players,
    BroadcastRunnerSignals signals,
    TimeProvider clock,
    DatabaseReadiness readiness,
    ILogger<BroadcastRunner> logger) : BackgroundService
{
    /// <summary>Attempts at one slot before it is skipped (03 §9.1).</summary>
    public const int MaxAttempts = 3;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    /// <summary>The longest the runner sleeps without re-reading the schedule, so that a missed signal costs little.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ErrorPause = TimeSpan.FromSeconds(10);

    private const string InterruptedNote = "interrupted by a restart";
    private const string StoppedNote = "stopped by the operator";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await readiness.Finished.WaitAsync(stoppingToken);
            if (readiness.Current.State != DatabaseState.Ready)
            {
                logger.LogWarning("The database is not ready, so the broadcast does not run");
                return;
            }

            var recovered = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!recovered)
                    {
                        await RecoverAsync(stoppingToken);
                        recovered = true;
                    }

                    await StepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "The broadcast step failed; it is retried");
                    await WaitAsync(clock.GetUtcNow() + ErrorPause, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping. The schedule stays Running, and the slot in play is skipped at the next start.
        }
    }

    /// <summary>One decision of the loop: wait, finish the schedule, or play the next slot.</summary>
    private async Task StepAsync(CancellationToken ct)
    {
        Guid scheduleId;
        Guid slotId;
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var schedule = await db.BroadcastSchedules
                .FirstOrDefaultAsync(s => s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused, ct);
            if (schedule is null || schedule.Status == ScheduleStatus.Paused)
            {
                await WaitAsync(clock.GetUtcNow() + MaxWait, ct);
                return;
            }

            var slot = await db.BroadcastSlots
                .Where(s => s.ScheduleId == schedule.Id && s.State == SlotState.Pending)
                .OrderBy(s => s.Index)
                .FirstOrDefaultAsync(ct);
            if (slot is null)
            {
                schedule.Status = ScheduleStatus.Finished;
                schedule.FinishedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                signals.Publish();
                logger.LogInformation("Broadcast schedule {ScheduleId} has played all its slots", schedule.Id);
                return;
            }

            if (slot.PlannedStart > clock.GetUtcNow())
            {
                await WaitAsync(slot.PlannedStart, ct);
                return;
            }

            scheduleId = schedule.Id;
            slotId = slot.Id;
        }

        await PlaySlotAsync(scheduleId, slotId, ct);
    }

    /// <summary>
    /// Plays one slot until it is finished or skipped, then holds for the game gap. A game that fails technically is aborted
    /// and its slot retried with a back-off (30 s, then doubling, capped at 10 min); the third failure skips the slot.
    /// </summary>
    private async Task PlaySlotAsync(Guid scheduleId, Guid slotId, CancellationToken ct)
    {
        while (true)
        {
            await signals.WaitWhilePausedAsync(ct);

            Attempt? attempt = null;
            GameEnd? end = null;
            bool stopped;
            using (var abort = new CancellationTokenSource())
            {
                // Registered before the game is created, so a stop that arrives at any point reaches it.
                signals.BeginGame(abort);
                try
                {
                    attempt = await StartAttemptAsync(scheduleId, slotId, ct);
                    if (attempt is null)
                    {
                        await FinishSlotAsync(slotId, SlotState.Skipped, StoppedNote, ct);
                        return;
                    }

                    var hooks = new GameRunHooks(
                        new TechnicalFailurePlayers(players),
                        new BroadcastPacer(attempt.MoveDelay, signals, clock));
                    end = await games.PlayToEndAsync(attempt.GameId, hooks, abort.Token, ct);
                }
                finally
                {
                    signals.EndGame(abort);
                }

                stopped = abort.IsCancellationRequested;
            }

            // The host stopping leaves the slot running; the next start skips it as interrupted.
            ct.ThrowIfCancellationRequested();

            if (stopped)
            {
                await FinishSlotAsync(slotId, SlotState.Skipped, StoppedNote, ct);
                return;
            }

            if (end is not null && end.Termination != GameTermination.Aborted.Termination)
            {
                await FinishSlotAsync(slotId, SlotState.Finished, null, ct);
                await HoldAsync(scheduleId, clock.GetUtcNow() + attempt!.Gap, ct);
                return;
            }

            // The game was aborted without a stop, or was not played: a technical failure.
            if (attempt!.SlotAttempts >= MaxAttempts)
            {
                logger.LogWarning("Broadcast slot {SlotId} is skipped after {Attempts} failed attempts", slotId, attempt.SlotAttempts);
                await FinishSlotAsync(slotId, SlotState.Skipped, $"skipped after {MaxAttempts} attempts: the model gave no reply", ct);
                await HoldAsync(scheduleId, clock.GetUtcNow() + attempt.Gap, ct);
                return;
            }

            var backoff = BackoffAfter(attempt.SlotAttempts);
            signals.Notice = $"Technical break: the model gave no reply. Retrying in {(int)backoff.TotalSeconds} s.";
            signals.Publish();
            logger.LogWarning("Broadcast slot {SlotId} failed technically; retrying in {Seconds} s", slotId, backoff.TotalSeconds);
            await HoldAsync(scheduleId, clock.GetUtcNow() + backoff, ct);
        }
    }

    /// <summary>Creates the slot's game and marks the slot running, or returns null when the schedule is no longer active.</summary>
    private async Task<Attempt?> StartAttemptAsync(Guid scheduleId, Guid slotId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        var slot = await db.BroadcastSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (schedule is null || slot is null
            || schedule.Status is not (ScheduleStatus.Running or ScheduleStatus.Paused)
            || slot.State is not (SlotState.Pending or SlotState.Running))
        {
            return null;
        }

        // The same shape as a game from POST /api/games: rated, from the starting position.
        var start = ChessBoard.StartingPosition().ToFen();
        var game = new GameEntity
        {
            Id = Guid.NewGuid(),
            WhiteKind = slot.WhiteKind,
            WhiteModel = slot.WhiteModel,
            BlackKind = slot.BlackKind,
            BlackModel = slot.BlackModel,
            Rated = true,
            Result = "*",
            StartFen = start,
            CurrentFen = start,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Games.Add(game);
        slot.State = SlotState.Running;
        slot.GameId = game.Id;
        slot.Attempts++;
        await db.SaveChangesAsync(ct);

        signals.Notice = null;
        signals.Publish();
        logger.LogInformation("Broadcast slot {SlotId} started as game {GameId} (attempt {Attempt})", slotId, game.Id, slot.Attempts);
        return new Attempt(
            game.Id,
            slot.Attempts,
            TimeSpan.FromMilliseconds(schedule.MoveDelayMs),
            TimeSpan.FromSeconds(schedule.GameGapSeconds));
    }

    /// <summary>Records the end of a slot. Only a running slot changes: a pending one stays pending when the schedule stops.</summary>
    private async Task FinishSlotAsync(Guid slotId, string state, string? note, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var slot = await db.BroadcastSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null || slot.State != SlotState.Running)
        {
            return;
        }

        slot.State = state;
        slot.Note = note;
        signals.Notice = null;
        await db.SaveChangesAsync(ct);
        signals.Publish();
    }

    /// <summary>Start-up: a slot that was running when the process stopped was cut off, so it is skipped (03 §9.3).</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = await db.BroadcastSchedules
            .Where(s => s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused)
            .ToListAsync(ct);
        var activeIds = active.Select(s => s.Id).ToList();
        var interrupted = await db.BroadcastSlots
            .Where(s => s.State == SlotState.Running && activeIds.Contains(s.ScheduleId))
            .ToListAsync(ct);

        foreach (var slot in interrupted)
        {
            slot.State = SlotState.Skipped;
            slot.Note = InterruptedNote;
        }

        await db.SaveChangesAsync(ct);
        signals.SetPaused(active.Any(s => s.Status == ScheduleStatus.Paused));
        if (interrupted.Count > 0)
        {
            logger.LogWarning("{Count} broadcast slot(s) were interrupted by a restart and are skipped", interrupted.Count);
        }
    }

    /// <summary>Holds until the time, while the schedule is still Running or Paused. A stop releases the hold at once.</summary>
    private async Task HoldAsync(Guid scheduleId, DateTimeOffset until, CancellationToken ct)
    {
        while (clock.GetUtcNow() < until && await ActiveAsync(scheduleId, ct))
        {
            await WaitAsync(until, ct);
        }
    }

    private async Task<bool> ActiveAsync(Guid scheduleId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.BroadcastSchedules.AnyAsync(
            s => s.Id == scheduleId && (s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused), ct);
    }

    /// <summary>
    /// Sleeps until the time, or until something is published (a command, a finished game), whichever comes first. The caller
    /// then re-reads the state; the sleep is capped so that a missed signal costs at most <see cref="MaxWait"/>.
    /// </summary>
    private async Task WaitAsync(DateTimeOffset until, CancellationToken ct)
    {
        var remaining = until - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        if (remaining > MaxWait)
        {
            remaining = MaxWait;
        }

        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(remaining, clock, wake.Token);
        await Task.WhenAny(signals.Changed, delay);
        wake.Cancel();
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>The back-off after the n-th failed attempt: 30 s, 1 min, 2 min … capped at 10 min (03 §9.1).</summary>
    private static TimeSpan BackoffAfter(int failures)
    {
        var backoff = TimeSpan.FromSeconds(FirstBackoff.TotalSeconds * Math.Pow(2, failures - 1));
        return backoff > MaxBackoff ? MaxBackoff : backoff;
    }

    /// <summary>What a slot attempt needs once its game exists.</summary>
    private sealed record Attempt(Guid GameId, int SlotAttempts, TimeSpan MoveDelay, TimeSpan Gap);
}
