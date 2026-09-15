using Comuki.Host.Mcp;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Mcp;

/// <summary>
/// The memory.note limiter in isolation: the per-worker window admits
/// <see cref="WorkerNoteRateLimiter.Limit"/> writes, rejects the next
/// one, and re-admits after the window slides past the oldest write.
/// </summary>
public sealed class WorkerNoteRateLimiterShould
{
    [Fact(DisplayName = "Given a fresh worker, when the limit is acquired Limit times, then the next acquire is rejected")]
    public void RejectsWritesBeyondTheLimit()
    {
        var clock = new SettableClock();
        var limiter = new WorkerNoteRateLimiter(clock);
        var workerId = WorkerId.New();

        var admitted = Enumerable.Range(0, WorkerNoteRateLimiter.Limit)
            .Select(_ => limiter.TryAcquire(workerId));

        admitted.ShouldAllBe(static admitted => admitted);
        limiter.TryAcquire(workerId).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a worker at the limit, when the window slides past the oldest write, then a new write is admitted again")]
    public void AdmitsAgainAfterTheWindowSlides()
    {
        var clock = new SettableClock();
        var limiter = new WorkerNoteRateLimiter(clock);
        var workerId = WorkerId.New();
        for (var acquired = 0; acquired < WorkerNoteRateLimiter.Limit; acquired++)
        {
            limiter.TryAcquire(workerId).ShouldBeTrue();
        }

        clock.Advance(WorkerNoteRateLimiter.Window + TimeSpan.FromSeconds(1));

        limiter.TryAcquire(workerId).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given two workers, when one hits the limit, then the other is unaffected")]
    public void WindowsArePerWorker()
    {
        var limiter = new WorkerNoteRateLimiter(new SettableClock());
        var spammy = WorkerId.New();
        var quiet = WorkerId.New();
        for (var acquired = 0; acquired < WorkerNoteRateLimiter.Limit; acquired++)
        {
            limiter.TryAcquire(spammy).ShouldBeTrue();
        }

        limiter.TryAcquire(spammy).ShouldBeFalse();
        limiter.TryAcquire(quiet).ShouldBeTrue();
    }
}

/// <summary>A time provider whose now the tests advance by hand.</summary>
internal sealed class SettableClock() : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan span)
    {
        now = now.Add(span);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
