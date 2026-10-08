using System.Threading.RateLimiting;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-worker sliding-window limiter for <c>memory.note</c> — the write
/// path a swarm worker has, so a runaway loop spamming facts cannot flood
/// the project corpus. One <see cref="PartitionedRateLimiter{TResource}"/>
/// keyed by <see cref="WorkerId"/> keeps the per-process state in a
/// framework-managed <see cref="SlidingWindowRateLimiter"/> (one partition
/// per worker); the host DI container owns its lifetime and disposal —
/// the limiter never holds a per-worker dictionary of its own.
/// </summary>
public sealed class WorkerNoteRateLimiter : IDisposable
{
    /// <summary>How many notes one worker may write per window.</summary>
    public const int Limit = 20;

    /// <summary>The sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Number of segments the window is divided into for the sliding average.</summary>
    public const int SegmentsPerWindow = 6;

    private readonly PartitionedRateLimiter<WorkerId> limiter;

    /// <summary>
    /// Builds the partitioned limiter. The factory is the same shape the host's
    /// <c>RateLimitInstaller</c> uses for HTTP partition keys, just keyed on
    /// <see cref="WorkerId"/> instead of an auth claim — the runtime contract
    /// (one window per worker, fixed permit count) is identical. The framework
    /// owns the wall clock; <see cref="SlidingWindowRateLimiter"/> ticks on the
    /// system time, so the limiter does not need a <see cref="TimeProvider"/>
    /// dependency.
    /// </summary>
    public WorkerNoteRateLimiter()
    {
        limiter = PartitionedRateLimiter.Create<WorkerId, WorkerId>(
            static workerId => RateLimitPartition.GetSlidingWindowLimiter(
                workerId,
                static _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = Limit,
                    Window = Window,
                    SegmentsPerWindow = SegmentsPerWindow,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));
    }

    /// <summary>
    /// Admits one note for the worker; <c>false</c> when the worker is at
    /// <see cref="Limit"/> inside the current <see cref="Window"/>. The
    /// returned lease is disposed before the call returns — a true admit
    /// is durable for the window, a false admit costs nothing.
    /// </summary>
    /// <param name="workerId">Worker whose window the request is charged against.</param>
    public bool TryAcquire(WorkerId workerId)
    {
        using var lease = limiter.AttemptAcquire(workerId, permitCount: 1);
        return lease.IsAcquired;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        limiter.Dispose();
    }
}
