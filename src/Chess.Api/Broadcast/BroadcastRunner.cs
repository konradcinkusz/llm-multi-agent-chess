using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Api.Requests;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chess.Api.Broadcast;

/// <summary>
/// Plays the broadcast one game at a time, paced for viewers (03 §9.1, ADR-011). A paid request in the queue goes first, and it
/// is played even with no schedule running (03 §15.1). Otherwise it plays the slots of the running schedule: it waits for each
/// slot's planned start, creates the game as a rated game, plays it with the schedule's move delay, then waits the game gap. A
/// game that gets no reply from the model is retried after a back-off; after <see cref="MaxAttempts"/> the slot is skipped and
/// the schedule moves on. Nothing that happens to one game ends the stream, and nothing escapes <see cref="ExecuteAsync"/>.
/// </summary>
public sealed class BroadcastRunner(
    IDbContextFactory<ArenaDbContext> factory,
    GameRunner games,
    IChessPlayerFactory players,
    BroadcastRequests requests,
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

    /// <summary>The pacing of a request game when no schedule is running: the defaults of 03 §9.1.</summary>
    private static readonly PacingProfile DefaultPacing = new(MoveDelayMs: 4000, GameGapSeconds: 20);

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

    /// <summary>
    /// One decision of the loop: wait, finish the schedule, play the next paid request, or play the next slot. A paused broadcast
    /// holds both. A request goes before the next planned slot, and it is taken with no schedule running too (03 §15.1).
    /// </summary>
    private async Task StepAsync(CancellationToken ct)
    {
        var active = await ActiveScheduleAsync(ct);
        if (active?.Status == ScheduleStatus.Paused)
        {
            await WaitAsync(clock.GetUtcNow() + MaxWait, ct);
            return;
        }

        if (await requests.TakeNextAsync(ct) is { } request)
        {
            await PlayRequestAsync(request, active?.Pacing ?? DefaultPacing, ct);
            return;
        }

        if (active is null)
        {
            await WaitAsync(clock.GetUtcNow() + MaxWait, ct);
            return;
        }

        Guid slotId;
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var slot = await db.BroadcastSlots
                .Where(s => s.ScheduleId == active.Id && s.State == SlotState.Pending)
                .OrderBy(s => s.Index)
                .FirstOrDefaultAsync(ct);
            if (slot is null)
            {
                var schedule = await db.BroadcastSchedules.SingleAsync(s => s.Id == active.Id, ct);
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

            slotId = slot.Id;
        }

        await PlaySlotAsync(active.Id, slotId, ct);
    }

    /// <summary>
    /// Plays one paid request as a rated game, paced like the broadcast and budgeted to its net price (03 §15.1, §15.2). The game
    /// is never interrupted by a request that arrives meanwhile. A game with no reply fails the request: the queue requeues it
    /// once, then credits it.
    /// </summary>
    private async Task PlayRequestAsync(QueuedRequest request, PacingProfile pacing, CancellationToken ct)
    {
        await signals.WaitWhilePausedAsync(ct);

        RequestGame? game;
        try
        {
            game = await requests.StartAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Request {RequestId} could not be started and is failed", request.Id);
            await requests.FailedAsync(request.Id, "could not be started", ct);
            await HoldAsync(null, clock.GetUtcNow() + FirstBackoff, ct);
            return;
        }

        // A refused request was credited when it was refused, so the stream moves on at once.
        if (game is null)
        {
            return;
        }

        signals.Notice = null;
        signals.Publish();

        GameEnd? end;
        bool stopped;
        using (var abort = new CancellationTokenSource())
        {
            signals.BeginGame(abort);
            try
            {
                var hooks = new GameRunHooks(
                    new TechnicalFailurePlayers(players),
                    new BroadcastPacer(TimeSpan.FromMilliseconds(pacing.MoveDelayMs), signals, clock),
                    game.Budget);
                end = await games.PlayToEndAsync(game.GameId, hooks, abort.Token, ct);
            }
            finally
            {
                signals.EndGame(abort);
            }

            stopped = abort.IsCancellationRequested;
        }

        // The host stopping leaves the request playing; the start-up recovery closes it through the queue.
        ct.ThrowIfCancellationRequested();

        if (!stopped && end is not null && end.Termination != GameTermination.Aborted.Termination)
        {
            await requests.PlayedAsync(request.Id, ct);
            await HoldAsync(null, clock.GetUtcNow() + TimeSpan.FromSeconds(pacing.GameGapSeconds), ct);
            return;
        }

        var reason = stopped ? StoppedNote : "the model gave no reply";
        await requests.FailedAsync(request.Id, reason, ct);
        logger.LogWarning("Request {RequestId} failed ({Reason}); the queue requeues it once, then credits it", request.Id, reason);
        signals.Notice = $"Technical break: a paid game could not be played. The next game starts in {(int)FirstBackoff.TotalSeconds} s.";
        signals.Publish();
        await HoldAsync(null, clock.GetUtcNow() + FirstBackoff, ct);
    }

    /// <summary>The schedule that is running or paused, with its pacing, or null when there is none.</summary>
    private async Task<ActiveSchedule?> ActiveScheduleAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var schedule = await db.BroadcastSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused, ct);
        return schedule is null
            ? null
            : new ActiveSchedule(schedule.Id, schedule.Status, new PacingProfile(schedule.MoveDelayMs, schedule.GameGapSeconds));
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

    /// <summary>
    /// Holds until the time, while the schedule is still Running or Paused. A stop releases the hold at once. With no schedule
    /// (a request played alone) the hold runs to the time.
    /// </summary>
    private async Task HoldAsync(Guid? scheduleId, DateTimeOffset until, CancellationToken ct)
    {
        while (clock.GetUtcNow() < until && (scheduleId is null || await ActiveAsync(scheduleId.Value, ct)))
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

    /// <summary>The schedule the runner is playing: running or paused, with its pacing.</summary>
    private sealed record ActiveSchedule(Guid Id, string Status, PacingProfile Pacing);
}
