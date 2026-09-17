using Comuki.Host.Mcp;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Mcp;

/// <summary>
/// The learning.suggest limiter in isolation: the per-worker window admits
/// <see cref="WorkerSuggestRateLimiter.Limit"/> suggestions, rejects the
/// next one, and re-admits after the window slides past the oldest one.
/// </summary>
public sealed class WorkerSuggestRateLimiterShould
{
    [Fact(DisplayName = "Given a fresh worker, when the limit is acquired Limit times, then the next acquire is rejected")]
    public void RejectsSuggestionsBeyondTheLimit()
    {
        var limiter = new WorkerSuggestRateLimiter(new SettableClock());
        var workerId = WorkerId.New();

        var admitted = Enumerable.Range(0, WorkerSuggestRateLimiter.Limit)
            .Select(_ => limiter.TryAcquire(workerId));

        admitted.ShouldAllBe(static admitted => admitted);
        limiter.TryAcquire(workerId).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a worker at the limit, when the window slides past the oldest suggestion, then a new one is admitted again")]
    public void AdmitsAgainAfterTheWindowSlides()
    {
        var clock = new SettableClock();
        var limiter = new WorkerSuggestRateLimiter(clock);
        var workerId = WorkerId.New();
        for (var acquired = 0; acquired < WorkerSuggestRateLimiter.Limit; acquired++)
        {
            limiter.TryAcquire(workerId).ShouldBeTrue();
        }

        clock.Advance(WorkerSuggestRateLimiter.Window + TimeSpan.FromSeconds(1));

        limiter.TryAcquire(workerId).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given two workers, when one hits the limit, then the other is unaffected")]
    public void WindowsArePerWorker()
    {
        var limiter = new WorkerSuggestRateLimiter(new SettableClock());
        var spammy = WorkerId.New();
        var quiet = WorkerId.New();
        for (var acquired = 0; acquired < WorkerSuggestRateLimiter.Limit; acquired++)
        {
            limiter.TryAcquire(spammy).ShouldBeTrue();
        }

        limiter.TryAcquire(spammy).ShouldBeFalse();
        limiter.TryAcquire(quiet).ShouldBeTrue();
    }
}
