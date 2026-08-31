namespace Comuki.Engine.Compute.Scaling;

/// <summary>
/// Pure scale policy v0 (issue #3, T2.4/T2.5): create-per-task — start one
/// idle worker per queued item the idle pool does not already cover, capped
/// by the project's <c>MaxConcurrent</c> minus running; reap stale idle
/// workers but never below <c>MinIdle</c>. No I/O — the supervisor feeds
/// counts and maps the decision to provider calls.
/// </summary>
public static class ScalePolicy
{
    /// <summary>
    /// Decides the desired pool change:
    /// <c>StartWorkers = clamp(QueuedCount - IdleCount, 0, MaxConcurrent - RunningCount)</c>
    /// and <c>StopIdleWorkers = clamp(min(StaleIdleCount, IdleCount - MinIdle), 0, ...)</c>.
    /// </summary>
    /// <param name="input"></param>
    public static ScaleDecision Decide(ScalePolicyInput input)
    {
        var startWorkers = Math.Clamp(
            input.QueuedCount - input.IdleCount,
            0,
            Math.Max(0, input.MaxConcurrent - input.RunningCount));
        var stopIdleWorkers = Math.Max(
            0,
            Math.Min(input.StaleIdleCount, input.IdleCount - input.MinIdle));

        return new ScaleDecision(startWorkers, stopIdleWorkers);
    }
}
