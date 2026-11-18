namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// A clock that moves only when the test fires its timers, so that a game paced at seconds per move runs in
/// microseconds. <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> waits on these timers.
/// </summary>
internal sealed class RunnerClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ArmedTimer> _timers = [];
    private DateTimeOffset _now = start;
    private long _changes;
    private long _settledChanges = -1;
    private long _settledSince;

    /// <summary>A jump of the clock at least this long waits until the timers have stood still for <see cref="SettleMilliseconds"/>.</summary>
    private static readonly TimeSpan SettleOver = TimeSpan.FromSeconds(10);

    private const long SettleMilliseconds = 20;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ArmedTimer(this, callback, state);
        timer.Schedule(dueTime);
        return timer;
    }

    /// <summary>
    /// Fires the earliest armed timer, moving the clock to its due time. Returns false when none is armed, and also when the
    /// jump would be long while timers are still changing: a signal may have woken a waiter whose continuation, still queued on
    /// the thread pool, is about to disarm its long timer (the runner's 30 s idle wait) and arm a short one (a 5 s gap). Firing
    /// the long timer first would move the clock past the gap. The caller waits a moment and asks again.
    /// </summary>
    public bool FireNext()
    {
        ArmedTimer next;
        lock (_gate)
        {
            if (_timers.Count == 0)
            {
                return false;
            }

            next = _timers.MinBy(t => t.Due)!;
            if (next.Due - _now >= SettleOver && !SettledLocked())
            {
                return false;
            }
            _timers.Remove(next);
            if (next.Due > _now)
            {
                _now = next.Due;
            }
        }

        next.Fire();
        return true;
    }

    /// <summary>Fires timers until <paramref name="done"/> holds, or fails after a real-time limit so that a hang is a failure.</summary>
    public async Task RunUntilAsync(Func<Task<bool>> done, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await done())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            if (!FireNext())
            {
                await Task.Delay(1);
            }
        }
    }

    /// <summary>Fires timers for a stretch of real time, to show that nothing happens while the broadcast waits.</summary>
    public async Task RunForAsync(TimeSpan realTime)
    {
        var until = DateTime.UtcNow + realTime;
        while (DateTime.UtcNow < until)
        {
            if (!FireNext())
            {
                await Task.Delay(1);
            }
        }
    }

    /// <summary>True once no timer has been armed or disarmed for <see cref="SettleMilliseconds"/> of real time.</summary>
    private bool SettledLocked()
    {
        var now = Environment.TickCount64;
        if (_changes != _settledChanges)
        {
            _settledChanges = _changes;
            _settledSince = now;
            return false;
        }

        return now - _settledSince >= SettleMilliseconds;
    }

    private void Arm(ArmedTimer timer)
    {
        lock (_gate)
        {
            _timers.Add(timer);
            _changes++;
        }
    }

    private void Disarm(ArmedTimer timer)
    {
        lock (_gate)
        {
            if (_timers.Remove(timer))
            {
                _changes++;
            }
        }
    }

    private sealed class ArmedTimer(RunnerClock clock, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset _due;
        private bool _armed;

        /// <summary>The due time while armed; the clock reads it under its lock.</summary>
        public DateTimeOffset Due => _due;

        public void Schedule(TimeSpan dueTime)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            lock (clock._gate)
            {
                _due = clock._now + dueTime;
            }

            clock.Arm(this);
            _armed = true;
        }

        public void Fire()
        {
            _armed = false;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock.Disarm(this);
            Schedule(dueTime);
            return true;
        }

        public void Dispose()
        {
            if (_armed)
            {
                clock.Disarm(this);
                _armed = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
