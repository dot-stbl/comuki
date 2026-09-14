namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// How the registry schedules one <see cref="IComukiWorker"/>: a fixed
/// poll <see cref="Interval(TimeSpan)"/> (first run immediately, then
/// every interval) or a one-shot <see cref="Startup()"/> run at boot.
/// </summary>
/// <remarks>
/// A channel-driven variant (execute per item off a
/// <c>Channel&lt;T&gt;</c>) is deliberately not modelled yet — the
/// existing channel-based hosted services stay bare BackgroundServices
/// until the follow-up conversion batch.
/// </remarks>
public abstract record WorkerSchedule
{
    /// <summary>Fixed poll schedule: run immediately, then every <paramref name="interval"/>.</summary>
    /// <param name="interval">Time between cycles; failures double it (capped at 10×) per consecutive failure.</param>
    /// <returns>The schedule.</returns>
    public static WorkerSchedule Interval(TimeSpan interval)
    {
        return new IntervalWorkerSchedule(interval);
    }

    /// <summary>Run-once-at-boot schedule; the registry does not reschedule after the cycle.</summary>
    /// <returns>The schedule.</returns>
    public static WorkerSchedule Startup()
    {
        return StartupWorkerSchedule.Instance;
    }

    /// <summary>Fixed poll interval.</summary>
    /// <param name="PollInterval">Time between cycles.</param>
    public sealed record IntervalWorkerSchedule(TimeSpan PollInterval) : WorkerSchedule;

    /// <summary>The run-once marker.</summary>
    public sealed record StartupWorkerSchedule : WorkerSchedule
    {
        /// <summary>The single instance.</summary>
        public static readonly StartupWorkerSchedule Instance = new();

        private StartupWorkerSchedule()
        {
        }
    }
}
