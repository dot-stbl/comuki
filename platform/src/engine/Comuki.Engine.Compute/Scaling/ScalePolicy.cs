namespace Comuki.Engine.Compute.Scaling;

/// <summary>
/// Pure scale policy v0 (issue #3, T2.4/T2.5; S8 quota awareness): 
/// create-per-task — start one idle worker per queued item the idle pool
/// does not already cover, capped by the project's <c>MaxConcurrent</c>
/// minus running AND by the provider's <c>FreeSlots</c> capacity hint;
/// reap stale idle workers but never below <c>MinIdle</c>. No I/O — the
/// supervisor feeds counts and maps the decision to provider calls.
/// </summary>
public static class ScalePolicy
{
    /// <summary>
    /// Decides the desired pool change:
    /// <c>StartWorkers = clamp(QueuedCount - IdleCount, 0, min(MaxConcurrent - RunningCount, FreeSlots))</c>
    /// and <c>StopIdleWorkers = clamp(min(StaleIdleCount, IdleCount - MinIdle), 0, ...)</c>.
    /// A null <c>FreeSlots</c> skips the capacity clamp.
    /// </summary>
    /// <param name="input"></param>
    public static ScaleDecision Decide(ScalePolicyInput input)
    {
        var concurrentCap = Math.Max(0, input.MaxConcurrent - input.RunningCount);
        var capacityCap = Math.Max(0, input.FreeSlots ?? int.MaxValue);
        var deficit = Math.Max(0, input.QueuedCount - input.IdleCount);

        var startWorkers = Math.Clamp(deficit, 0, Math.Min(concurrentCap, capacityCap));
        var clampedByCapacity = capacityCap < concurrentCap && deficit > capacityCap;
        var stopIdleWorkers = Math.Max(
            0,
            Math.Min(input.StaleIdleCount, input.IdleCount - input.MinIdle));

        return new ScaleDecision(startWorkers, stopIdleWorkers, clampedByCapacity);
    }
}
