using Comuki.Host.Mcp;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Mcp;

/// <summary>
/// The memory.note limiter in isolation: the per-worker sliding window
/// (delegated to <c>System.Threading.RateLimiting</c>) admits
/// <see cref="WorkerNoteRateLimiter.Limit"/> writes, rejects the next one,
/// and isolates partitions per worker. The wall-clock bookkeeping belongs
/// to the framework's <c>SlidingWindowRateLimiter</c> — we exercise only
/// the per-call contract here; the framework's own unit tests cover the
/// time-slide behaviour.
/// </summary>
public sealed class WorkerNoteRateLimiterShould
{
    [Fact(DisplayName = "Given a fresh worker, when the limit is acquired Limit times, then the next acquire is rejected")]
    public void RejectsWritesBeyondTheLimit()
    {
        using var limiter = new WorkerNoteRateLimiter();
        var workerId = WorkerId.New();

        var admitted = Enumerable.Range(0, WorkerNoteRateLimiter.Limit)
            .Select(_ => limiter.TryAcquire(workerId));

        admitted.ShouldAllBe(static admitted => admitted);
        limiter.TryAcquire(workerId).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given two workers, when one hits the limit, then the other is unaffected")]
    public void WindowsArePerWorker()
    {
        using var limiter = new WorkerNoteRateLimiter();
        var spammy = WorkerId.New();
        var quiet = WorkerId.New();
        for (var acquired = 0; acquired < WorkerNoteRateLimiter.Limit; acquired++)
        {
            limiter.TryAcquire(spammy).ShouldBeTrue();
        }

        limiter.TryAcquire(spammy).ShouldBeFalse();
        limiter.TryAcquire(quiet).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given the limiter, when two workers acquire in alternation, then neither crosses the other's window")]
    public void AlternatingWorkersAreIndependent()
    {
        using var limiter = new WorkerNoteRateLimiter();
        var first = WorkerId.New();
        var second = WorkerId.New();

        // Each worker gets its full budget without the other one affecting
        // it - exercises the PartitionedRateLimiter keyed on WorkerId.
        for (var round = 0; round < WorkerNoteRateLimiter.Limit; round++)
        {
            limiter.TryAcquire(first).ShouldBeTrue();
            limiter.TryAcquire(second).ShouldBeTrue();
        }

        // Both are now at their respective limits.
        limiter.TryAcquire(first).ShouldBeFalse();
        limiter.TryAcquire(second).ShouldBeFalse();
    }
}
