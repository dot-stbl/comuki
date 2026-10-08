using System.Threading.RateLimiting;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-worker sliding-window limiter for <c>learning.suggest</c> — the
/// learning queue is human-reviewed, so a runaway worker spamming
/// suggestions floods the approvers' queue, not just its own project. The
/// mission budget is "max 5 per worker per run"; the caller carries no run
/// id, so the closest per-process approximation is the same shape as
/// <see cref="WorkerNoteRateLimiter"/> — a per-worker sliding window with
/// a tight limit over a window long enough to span a typical run.
/// </summary>
public sealed class WorkerSuggestRateLimiter : IDisposable
{
    /// <summary>How many suggestions one worker may queue per window.</summary>
    public const int Limit = 5;

    /// <summary>The sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    /// <summary>Number of segments the window is divided into for the sliding average.</summary>
    public const int SegmentsPerWindow = 6;

    private readonly PartitionedRateLimiter<WorkerId> limiter;

    /// <summary>
    /// Builds the partitioned limiter. Same shape as
    /// <see cref="WorkerNoteRateLimiter"/> — different window and limit, same
    /// contract. Framework-owned wall clock; no <see cref="TimeProvider"/>
    /// dependency.
    /// </summary>
    public WorkerSuggestRateLimiter()
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
    /// Admits one suggestion for the worker; <c>false</c> when the worker is
    /// at <see cref="Limit"/> inside the current <see cref="Window"/>. The
    /// returned lease is disposed before the call returns.
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
