using Comuki.Engine.Compute.Scaling;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Truth table for the pure scale policy: create-per-task capped by
/// MaxConcurrent, stale-idle reaping floored by MinIdle.
/// </summary>
public sealed class ScalePolicyShould
{
    [Theory(DisplayName = "Given counts, when Decide is called, then returns the expected start/stop delta")]
    [InlineData(3, 0, 0, 0, 0, 5, 3, 0)]    // DoD: 3 queued items, empty pool — start 3
    [InlineData(3, 2, 0, 2, 0, 5, 1, 0)]    // idle pool covers part of the backlog — start the deficit
    [InlineData(2, 5, 0, 5, 0, 8, 0, 0)]    // idle pool covers the backlog — start nothing
    [InlineData(5, 0, 0, 4, 0, 4, 0, 0)]    // MaxConcurrent reached — start nothing
    [InlineData(1, 0, 0, 2, 0, 3, 1, 0)]    // single queued item below the cap
    [InlineData(0, 5, 0, 5, 1, 8, 0, 0)]    // idle workers exist but none stale — stop nothing
    [InlineData(0, 4, 4, 4, 1, 8, 0, 3)]    // all idle stale — reap down to the MinIdle floor
    [InlineData(0, 4, 2, 4, 0, 8, 0, 2)]    // only the stale ones are reaped
    [InlineData(0, 2, 2, 2, 2, 8, 0, 0)]    // the floor protects every idle worker
    [InlineData(0, 1, 1, 1, 0, 2, 0, 1)]    // single stale idle worker is reaped
    [InlineData(2, 2, 1, 2, 0, 4, 0, 1)]    // no deficit AND a stale worker — reap only
    [InlineData(3, 1, 1, 1, 1, 10, 2, 0)]   // deficit computed after idle coverage, stale one kept by floor
    public void ReturnExpectedDecision(
        int queuedCount,
        int idleCount,
        int staleIdleCount,
        int runningCount,
        int minIdle,
        int maxConcurrent,
        int expectedStart,
        int expectedStop)
    {
        var input = new ScalePolicyInput(queuedCount, idleCount, staleIdleCount, runningCount, minIdle, maxConcurrent);

        var decision = ScalePolicy.Decide(input);

        decision.StartWorkers.ShouldBe(expectedStart);
        decision.StopIdleWorkers.ShouldBe(expectedStop);
    }

    [Fact]
    public void ClampStartToZeroWhenRunningExceedsMaxConcurrent()
    {
        var input = new ScalePolicyInput(QueuedCount: 4, IdleCount: 0, StaleIdleCount: 0, RunningCount: 6, MinIdle: 0, MaxConcurrent: 4);

        var decision = ScalePolicy.Decide(input);

        decision.StartWorkers.ShouldBe(0);
        decision.StopIdleWorkers.ShouldBe(0);
    }
}
