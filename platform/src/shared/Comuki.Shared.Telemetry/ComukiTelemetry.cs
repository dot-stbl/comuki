using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Comuki.Shared.Telemetry;

/// <summary>
/// The process-wide Comuki meters and activity sources, created once so the
/// OpenTelemetry SDK subscription (by name, in the telemetry installer)
/// matches the instances the call sites record to. Instruments are created
/// eagerly — an instrument without a subscriber is a cheap no-op.
/// </summary>
public static class ComukiTelemetry
{
    /// <summary>Activity source of the orchestration engine (claim span).</summary>
    public static ActivitySource OrchestrationSource { get; } = new(ComukiInstrumentation.OrchestrationSourceName);

    /// <summary>Activity source of the compute engine (worker start/stop spans).</summary>
    public static ActivitySource ComputeSource { get; } = new(ComukiInstrumentation.ComputeSourceName);

    /// <summary>Activity source of the orchestrator host (apply-plan span).</summary>
    public static ActivitySource HostSource { get; } = new(ComukiInstrumentation.HostSourceName);

    /// <summary>Meter of the work-queue surface: claims, backlog.</summary>
    public static Meter QueueMeter { get; } = new(ComukiInstrumentation.QueueMeterName);

    /// <summary>Meter of run creation: runs started, work items queued.</summary>
    public static Meter RunsMeter { get; } = new(ComukiInstrumentation.RunsMeterName);

    /// <summary>Meter of the compute layer: worker lifecycle.</summary>
    public static Meter ComputeMeter { get; } = new(ComukiInstrumentation.ComputeMeterName);

    /// <summary>Wall time of one work-item claim attempt, milliseconds.</summary>
    public static Histogram<double> ClaimDuration { get; } =
        QueueMeter.CreateHistogram<double>(ComukiInstrumentation.ClaimDurationName, unit: "ms");

    /// <summary>Claim attempts by outcome (hit / empty).</summary>
    public static Counter<long> Claims { get; } =
        QueueMeter.CreateCounter<long>(ComukiInstrumentation.ClaimedCountName);

    /// <summary>Runs created from approved plans.</summary>
    public static Counter<long> RunsStarted { get; } =
        RunsMeter.CreateCounter<long>(ComukiInstrumentation.RunStartedName);

    /// <summary>Work items queued into new runs.</summary>
    public static Counter<long> WorkItemsQueued { get; } =
        RunsMeter.CreateCounter<long>(ComukiInstrumentation.WorkItemsQueuedName);

    /// <summary>Wall time of one worker start call, milliseconds.</summary>
    public static Histogram<double> WorkerStartDuration { get; } =
        ComputeMeter.CreateHistogram<double>(ComukiInstrumentation.WorkerStartDurationName, unit: "ms");

    /// <summary>Worker runtimes started, by provider and profile.</summary>
    public static Counter<long> WorkersStarted { get; } =
        ComputeMeter.CreateCounter<long>(ComukiInstrumentation.WorkersStartedName);

    /// <summary>Worker runtimes stopped, by provider and reason.</summary>
    public static Counter<long> WorkersStopped { get; } =
        ComputeMeter.CreateCounter<long>(ComukiInstrumentation.WorkersStoppedName);

    /// <summary>
    /// Records one claim outcome: duration histogram, the claim counter with a
    /// bounded outcome tag, and the tag on the caller's span when one is open.
    /// Call sites pass the activity they started (or null when unlistened).
    /// </summary>
    /// <param name="duration">Wall time of the claim attempt.</param>
    /// <param name="claimedItem">Whether a work item was claimed (hit) or not (empty).</param>
    /// <param name="activity">The claim activity started by the call site, if any.</param>
    public static void RecordClaim(TimeSpan duration, bool claimedItem, Activity? activity)
    {
        var outcome = claimedItem ? ComukiInstrumentation.OutcomeHit : ComukiInstrumentation.OutcomeEmpty;
        ClaimDuration.Record(duration.TotalMilliseconds);
        Claims.Add(1, new KeyValuePair<string, object?>(ComukiInstrumentation.OutcomeTag, outcome));
        activity?.SetTag(ComukiInstrumentation.OutcomeTag, outcome);
    }
}
