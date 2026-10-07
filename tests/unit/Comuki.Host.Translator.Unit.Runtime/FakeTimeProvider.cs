namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Deterministic clock for watchdog / deadline-policy unit tests —
/// the production code reads time exclusively through the injected
/// <see cref="TimeProvider"/>, so a test advances it instead of
/// sleeping for real. Mirrors the shape of the shared testing-infra
/// <c>FakeTimeProvider</c> in <c>Comuki.Host.Testing.Clocks</c>
/// (mirrored here because this is a unit-test project, not
/// integration, and unit tests must run without a Testcontainers
/// runtime).
/// <para>
/// Implements <see cref="TimeProvider.CreateTimer"/> so the
/// <c>WorkerProgressWatchdog</c> and <c>DeadlinePolicy</c> can
/// drive their ticks through <see cref="ITimer"/> (the .NET 8+
/// virtual timer). Tests advance time via <see cref="Advance"/>;
/// the provider fires every timer whose <c>dueTime</c> has
/// elapsed, in registration order. No <c>Task.Delay</c> in tests
/// — the timer fires synchronously on advance.
/// </para>
/// </summary>
/// <remarks>Constructs a clock pinned to the explicit starting instant.</remarks>
/// <param name="initial">The reading <see cref="GetUtcNow"/> returns until the next <see cref="Advance"/>.</param>
internal sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset utcNow = initial;
    private readonly List<FakeTimer> activeTimers = [];

    /// <summary>Constructs a clock pinned to a fixed starting instant.</summary>
    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero))
    {
    }

    /// <summary>Moves the clock forward by <paramref name="duration"/> and fires every timer whose <c>dueTime</c> has elapsed. A periodic timer with a small period fires multiple times — each <c>Fire</c> rolls the next <c>dueAt</c> by the period; we keep firing until the next <c>dueAt</c> is past the new clock reading.</summary>
    /// <param name="duration">Wall-clock duration to add to the current reading.</param>
    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var target = utcNow.Add(duration);

        // Fire timers in registration order. For each timer that
        // is past its DueAt, set utcNow to DueAt (so the callback's
        // clock.GetUtcNow() returns the tick's wall-clock reading,
        // not the Advance's final target) and call Fire(). The
        // periodic Fire rolls DueAt forward by the period; we loop
        // until no more timers are past their DueAt. The final
        // utcNow lands on the target.
        //
        // Bounded loop guard: a misconfigured timer with a tiny
        // period could spin; cap at a hard ceiling.
        var iterations = 0;
        const int MaxIterations = 100_000;
        while (iterations++ < MaxIterations)
        {
            var snapshot = activeTimers.ToArray();
            FakeTimer? nextFirable = null;
            foreach (var timer in snapshot)
            {
                if (timer.IsDisposed)
                {
                    continue;
                }

                if (timer.DueAt > target)
                {
                    continue;
                }

                if (nextFirable is null || timer.DueAt < nextFirable.DueAt)
                {
                    nextFirable = timer;
                }
            }

            if (nextFirable is null)
            {
                break;
            }

            // Move the clock to this tick's wall-clock reading
            // before firing, so the callback's clock.GetUtcNow()
            // returns the moment of the tick, not the final target.
            utcNow = nextFirable.DueAt;
            nextFirable.Fire();
        }

        utcNow = target;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var dueAt = dueTime == Timeout.InfiniteTimeSpan
            ? DateTimeOffset.MaxValue
            : utcNow.Add(dueTime);
        var timer = new FakeTimer(this, callback, state, dueAt, period);
        activeTimers.Add(timer);
        return timer;
    }

    private void OnTimerDisposed(FakeTimer timer)
    {
        activeTimers.Remove(timer);
    }

    /// <summary>Test double backing <see cref="CreateTimer"/>; records the
    /// <c>dueAt</c> wall-clock reading and the period, and fires the
    /// wrapped callback when <see cref="Advance"/> crosses the
    /// deadline. Disposal unregisters the timer so the test does
    /// not hold a reference past its consumer's lifetime.</summary>
    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset dueAt, TimeSpan period) : ITimer
    {
        private readonly FakeTimeProvider owner = owner;
        private readonly TimerCallback callback = callback;
        private readonly object? state = state;
        private TimeSpan period = period;

        public DateTimeOffset DueAt { get; private set; } = dueAt;

        public bool IsDisposed { get; private set; }

        public void Fire()
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                callback(state);
            }
            finally
            {
                if (period > TimeSpan.Zero && !IsDisposed)
                {
                    DueAt = DueAt.Add(period);
                }
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed)
            {
                return false;
            }

            DueAt = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : owner.utcNow.Add(dueTime);
            this.period = period;
            return true;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            owner.OnTimerDisposed(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
