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
/// Plays the broadcast one game at a time, paced for viewers (03 §9.1, ADR-011). It does nothing while no schedule is running:
/// a paid request waits in the queue until a schedule runs (03 §15.1) and then goes before the next slot. Otherwise it plays the
/// slots of the running schedule in order: it creates the game as a rated game, plays it with the schedule's move delay, then
/// waits the game gap. The planned times of the slots are estimates, not appointments: the runner never waits for a clock time,
/// and whenever a slot leaves Running (finished, skipped, recovered) it re-times the pending slots and the schedule's end from
/// that moment plus the gap. A game that gets no reply from the model is retried after a back-off; after
/// <see cref="MaxAttempts"/> the slot is skipped and the schedule moves on. Nothing that happens to one game ends the stream, and
/// nothing escapes <see cref="ExecuteAsync"/>.
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

    /// <summary>How long the runner waits before it takes a paid request again that waits for the model catalog.</summary>
    private static readonly TimeSpan CatalogRetryWait = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ErrorPause = TimeSpan.FromSeconds(10);

    private const string InterruptedNote = "interrupted by a restart";
    private const string OrphanNote = "interrupted by an error";
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
    /// holds both. A request goes before the next planned slot. Requests wait while no schedule is running or paused, so a
    /// finished broadcast does not start a game on its own: a waiting request stays queued and is not lost.
    /// </summary>
    private async Task StepAsync(CancellationToken ct)
    {
        // Taken before the state is read, so a command published while it is read still wakes the wait below.
        var changed = signals.Changed;
        var active = await ActiveScheduleAsync(ct);
        if (active?.Status == ScheduleStatus.Paused)
        {
            await WaitAsync(clock.GetUtcNow() + MaxWait, ct, changed);
            return;
        }

        if (active is null)
        {
            await WaitAsync(clock.GetUtcNow() + MaxWait, ct, changed);
            return;
        }

        // A slot still Running here has no game being played: an error cut the runner off after the slot started.
        if (await RecoverOrphanAsync(active, ct))
        {
            return;
        }

        if (await requests.TakeNextAsync(ct) is { } request)
        {
            await PlayRequestAsync(request, active, ct);
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

            slotId = slot.Id;
        }

        await PlaySlotAsync(active.Id, slotId, ct);
    }

    /// <summary>
    /// Plays one paid request as a rated game, paced like the broadcast and budgeted to its net price (03 §15.1, §15.2). The game
    /// is never interrupted by a request that arrives meanwhile. A game with no reply fails the request: the queue requeues it
    /// once, then credits it.
    /// </summary>
    private async Task PlayRequestAsync(QueuedRequest request, ActiveSchedule active, CancellationToken ct)
    {
        var pacing = active.Pacing;
        await signals.WaitWhilePausedAsync(ct);

        RequestGame? game;
        try
        {
            game = await requests.StartAsync(request, ct);
        }
        catch (CatalogNotReadyException ex) when (!ct.IsCancellationRequested)
        {
            // Not the request's fault, and it must not spend one of its two attempts: back in the queue, then look again soon.
            logger.LogWarning("Request {RequestId} waits: {Reason}", request.Id, ex.Message);
            await requests.ReleaseAsync(request.Id, "waiting for the model catalog", ct);
            signals.Notice = "A paid game is waiting for the model catalog to be ready.";
            signals.Publish();
            await WaitAsync(clock.GetUtcNow() + CatalogRetryWait, ct);
            return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Request {RequestId} could not be started and is failed", request.Id);
            await requests.FailedAsync(request.Id, "could not be started", ct);
            await HoldAfterRequestAsync(active.Id, FirstBackoff, pacing, ct);
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
            await HoldAfterRequestAsync(active.Id, TimeSpan.FromSeconds(pacing.GameGapSeconds), pacing, ct);
            return;
        }

        var reason = stopped ? StoppedNote : "the model gave no reply";
        await requests.FailedAsync(request.Id, reason, ct);
        logger.LogWarning("Request {RequestId} failed ({Reason}); the queue requeues it once, then credits it", request.Id, reason);
        signals.Notice = $"Technical break: a paid game could not be played. The next game starts in {(int)FirstBackoff.TotalSeconds} s.";
        signals.Publish();
        await HoldAfterRequestAsync(active.Id, FirstBackoff, pacing, ct);
    }

    /// <summary>
    /// The pause after a request game: the pending slots are re-timed to start when it ends, then the runner holds, and a stop
    /// of the schedule ends the hold.
    /// </summary>
    private async Task HoldAfterRequestAsync(Guid scheduleId, TimeSpan hold, PacingProfile pacing, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now + hold;
        await RetimeAsync(scheduleId, null, now, until, TimeSpan.FromSeconds(pacing.GameGapSeconds), ct);
        await HoldAsync(scheduleId, until, ct);
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
                var endedAt = clock.GetUtcNow();
                var nextStart = endedAt + attempt!.Gap;
                await RetimeAsync(scheduleId, slotId, endedAt, nextStart, attempt.Gap, ct);
                await HoldAsync(scheduleId, nextStart, ct);
                return;
            }

            // The game was aborted without a stop, or was not played: a technical failure.
            if (attempt!.SlotAttempts >= MaxAttempts)
            {
                logger.LogWarning("Broadcast slot {SlotId} is skipped after {Attempts} failed attempts", slotId, attempt.SlotAttempts);
                await FinishSlotAsync(slotId, SlotState.Skipped, $"skipped after {MaxAttempts} attempts: the model gave no reply", ct);
                var skippedAt = clock.GetUtcNow();
                var nextAfterSkip = skippedAt + attempt.Gap;
                await RetimeAsync(scheduleId, slotId, skippedAt, nextAfterSkip, attempt.Gap, ct);
                await HoldAsync(scheduleId, nextAfterSkip, ct);
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

    /// <summary>
    /// Re-times the schedule after a slot left Running, while the schedule is still running or paused. The slot that left
    /// (<paramref name="leftSlotId"/>, when there is one) ends at <paramref name="endedAt"/>, the first pending slot is
    /// planned at <paramref name="firstStart"/> and the rest follow back to back with their planned lengths and the gap, and
    /// the schedule's end becomes the end of its last slot. These are estimates that the page shows; the runner plays the
    /// next slot when the hold ends, not at the planned time.
    /// </summary>
    private async Task RetimeAsync(
        Guid scheduleId, Guid? leftSlotId, DateTimeOffset endedAt, DateTimeOffset firstStart, TimeSpan gap, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null || schedule.Status is not (ScheduleStatus.Running or ScheduleStatus.Paused))
        {
            return;
        }

        var slots = await db.BroadcastSlots.Where(s => s.ScheduleId == scheduleId).OrderBy(s => s.Index).ToListAsync(ct);
        RetimeSlots(schedule, slots, leftSlotId, endedAt, firstStart, gap);
        await db.SaveChangesAsync(ct);
        signals.Publish();
    }

    /// <summary>The arithmetic of <see cref="RetimeAsync"/>, on slots already loaded; the caller saves.</summary>
    private static void RetimeSlots(
        BroadcastScheduleEntity schedule, List<BroadcastSlotEntity> slots, Guid? leftSlotId,
        DateTimeOffset endedAt, DateTimeOffset firstStart, TimeSpan gap)
    {
        if (leftSlotId is { } leftId && slots.FirstOrDefault(s => s.Id == leftId) is { } left)
        {
            left.PlannedEnd = endedAt;
        }

        var next = firstStart;
        foreach (var slot in slots.Where(s => s.State == SlotState.Pending))
        {
            var length = slot.PlannedEnd - slot.PlannedStart;
            slot.PlannedStart = next;
            slot.PlannedEnd = next + length;
            next = slot.PlannedEnd + gap;
        }

        if (slots.Count > 0)
        {
            schedule.EndsAt = slots.Max(s => s.PlannedEnd);
        }
    }

    /// <summary>
    /// A slot that is Running when the runner is about to pick one has no game being played, because the runner plays one game
    /// at a time and an error cut it off after the slot started (a database failure past the retries). The slot is settled from
    /// its game: a finished game finishes it, an aborted game retries it (or skips it after <see cref="MaxAttempts"/>), and a
    /// game left unfinished is closed as interrupted and the slot skipped, so <c>now</c> never follows a dead game.
    /// </summary>
    /// <returns>True when a slot was settled and the step is over.</returns>
    private async Task<bool> RecoverOrphanAsync(ActiveSchedule active, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var slot = await db.BroadcastSlots
            .Where(s => s.ScheduleId == active.Id && s.State == SlotState.Running)
            .OrderBy(s => s.Index)
            .FirstOrDefaultAsync(ct);
        if (slot is null)
        {
            return false;
        }

        var game = slot.GameId is { } gameId ? await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, ct) : null;
        var now = clock.GetUtcNow();
        var gap = TimeSpan.FromSeconds(active.Pacing.GameGapSeconds);
        var endedAt = now;
        var holdUntil = now;
        var retime = true;

        if (game?.FinishedAt is { } finishedAt && game.Termination != GameTermination.Aborted.Termination)
        {
            slot.State = SlotState.Finished;
            slot.Note = null;
            endedAt = finishedAt;
            holdUntil = finishedAt + gap;
        }
        else if (game?.FinishedAt is not null && slot.Attempts < MaxAttempts)
        {
            slot.State = SlotState.Pending;
            retime = false;
        }
        else
        {
            if (game is not null && game.FinishedAt is null)
            {
                game.Result = "*";
                game.Termination = InterruptedGameSweep.Termination;
                game.FinishedAt = now;
            }

            slot.State = SlotState.Skipped;
            slot.Note = OrphanNote;
        }

        if (retime)
        {
            var schedule = await db.BroadcastSchedules.SingleAsync(s => s.Id == active.Id, ct);
            var slots = await db.BroadcastSlots.Where(s => s.ScheduleId == active.Id).OrderBy(s => s.Index).ToListAsync(ct);
            RetimeSlots(schedule, slots, slot.Id, endedAt, holdUntil, gap);
        }

        signals.Notice = null;
        await db.SaveChangesAsync(ct);
        signals.Publish();
        logger.LogWarning("Broadcast slot {SlotId} was left running by an error and is now {State}", slot.Id, slot.State);
        await HoldAsync(active.Id, holdUntil, ct);
        return true;
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

        var now = clock.GetUtcNow();
        foreach (var slot in interrupted)
        {
            slot.State = SlotState.Skipped;
            slot.Note = InterruptedNote;
        }

        // The pending slots were planned for a clock that has moved on: they play at once, so re-time them from now.
        foreach (var schedule in active)
        {
            var slots = await db.BroadcastSlots.Where(s => s.ScheduleId == schedule.Id).OrderBy(s => s.Index).ToListAsync(ct);
            var left = interrupted.FirstOrDefault(s => s.ScheduleId == schedule.Id);
            RetimeSlots(schedule, slots, left?.Id, now, now, TimeSpan.FromSeconds(schedule.GameGapSeconds));
        }

        await db.SaveChangesAsync(ct);
        signals.SetPaused(active.Any(s => s.Status == ScheduleStatus.Paused));
        if (interrupted.Count > 0)
        {
            logger.LogWarning("{Count} broadcast slot(s) were interrupted by a restart and are skipped", interrupted.Count);
        }
    }

    /// <summary>
    /// Holds until the time, while the schedule is still Running or Paused. A stop releases the hold at once, so a day stopped
    /// in a gap and a new day started do not wait out the old gap.
    /// </summary>
    private async Task HoldAsync(Guid scheduleId, DateTimeOffset until, CancellationToken ct)
    {
        while (true)
        {
            // Taken before the schedule is read, so a stop published while it is read still ends the hold.
            var changed = signals.Changed;
            if (clock.GetUtcNow() >= until || !await ActiveAsync(scheduleId, ct))
            {
                return;
            }

            await WaitAsync(until, ct, changed);
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
    /// <paramref name="changed"/> is the signal the caller took before it read the state: a publish between that read and this
    /// wait has completed it, so the wait returns at once instead of sleeping through the change. Without it the wait takes the
    /// current signal, which is right only when nothing was read that a publish could have changed.
    /// </summary>
    private async Task WaitAsync(DateTimeOffset until, CancellationToken ct, Task? changed = null)
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
        await Task.WhenAny(changed ?? signals.Changed, delay);
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
