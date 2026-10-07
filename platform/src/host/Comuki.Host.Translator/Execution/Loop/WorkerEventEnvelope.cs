using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Shared.Contracts.Grpc;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// Stage-condition / envelope builders over a claim and an outcome.
/// Conditions are sandbox stage flips (harden-pi-worker-sandbox 5.1,
/// spec D6): <c>WorkspacePrepared</c>, <c>EgressApplied</c>,
/// <c>AgentRunning</c>. The host journals them as <c>worker.condition</c>
/// entries with a <c>{ name, value }</c> payload. The drain envelope
/// is the pre-complete artifact flush (harden-pi-worker-sandbox 5.2,
/// spec D7); the host packager picks it up and skips prefixes it has
/// already bundled.
/// </summary>
public static class WorkerEventEnvelope
{
    /// <summary>The first event of a run: which item, which run, what brief.</summary>
    public static WorkerEvent ToStartEvent(ClaimedWorkItemResponse claimed)
    {
        return new WorkerEvent
        {
            Start = new StageStart
            {
                WorkItemId = claimed.WorkItemId.ToString(),
                RunId = claimed.RunId.ToString(),
                Brief = claimed.Brief,
            },
        };
    }

    /// <summary>The last event of a run: the bottom line.</summary>
    public static WorkerEvent ToReportEvent(Guid workItemId, PiOutcome outcome)
    {
        return new WorkerEvent
        {
            Report = new StageReport
            {
                WorkItemId = workItemId.ToString(),
                Status = outcome.Status,
                DurationMs = outcome.DurationMs,
                ResultText = outcome.ResultText,
                ErrorText = outcome.ErrorText,
            },
        };
    }

    /// <summary>
    /// A boolean condition flip: the worker's <c>name</c> reached
    /// <paramref name="value"/>. The host journal entry mirrors the
    /// <c>name</c>/<c>value</c> pair exactly so the dashboard can render
    /// <c>WorkspacePrepared</c>/<c>EgressApplied</c>/<c>AgentRunning</c>
    /// as their own column without parsing the worker.reported payload.
    /// </summary>
    /// <param name="workItemId">Work item the condition binds to.</param>
    /// <param name="name">Sandbox condition name (<c>WorkspacePrepared</c>, etc.).</param>
    /// <param name="value">Boolean state the condition flipped to.</param>
    public static WorkerEvent ToConditionEvent(Guid workItemId, string name, bool value)
    {
        return new WorkerEvent
        {
            Condition = new StageCondition
            {
                WorkItemId = workItemId.ToString(),
                Name = name,
                Value = value,
            },
        };
    }

    /// <summary>
    /// Pre-complete artifact drain (harden-pi-worker-sandbox 5.2,
    /// spec D7): the Translator sends this right before
    /// <c>api.CompleteAsync</c> / <c>api.FailAsync</c>. The host-side
    /// packager reads <paramref name="artifacts"/> and skips prefixes it
    /// has already bundled; the worker side never blocks on a drain
    /// failure (logged at warning, see
    /// <see cref="TranslatorLoop.TryRunOnceAsync"/>).
    /// </summary>
    /// <param name="workItemId">Work item the drain belongs to.</param>
    /// <param name="artifacts">Object names under the run prefix.</param>
    public static WorkerEvent ToDrainEvent(Guid workItemId, IReadOnlyList<string> artifacts)
    {
        return new WorkerEvent
        {
            Drain = new StageDrain
            {
                WorkItemId = workItemId.ToString(),
                Artifacts = artifacts,
            },
        };
    }

    /// <summary>
    /// Stall-warning event (harden-worker-runtime Phase 1, design D1).
    /// The <c>WorkerProgressWatchdog</c> fires this on tier 1 of the
    /// escalation chain — log + journal <c>worker.stall_warn</c>, no
    /// action. The host journal reads <c>last_event_age_ms</c> and
    /// <c>tier</c> off the payload so the dashboard can render the
    /// stall column without re-parsing the run's timeline.
    /// </summary>
    /// <param name="workItemId">Work item the stall is bound to.</param>
    /// <param name="lastEventAgeMs">Time since the last parsed stream-event, in milliseconds.</param>
    /// <param name="tier">Escalation tier (1 = warn).</param>
    public static WorkerEvent ToStallWarnEvent(Guid workItemId, long lastEventAgeMs, int tier)
    {
        return new WorkerEvent
        {
            StallWarn = new StageStallWarn
            {
                WorkItemId = workItemId.ToString(),
                LastEventAgeMs = lastEventAgeMs,
                Tier = tier,
            },
        };
    }

    /// <summary>
    /// Stall-detected event (harden-worker-runtime Phase 1, design D1 + D2).
    /// The <c>WorkerProgressWatchdog</c> / <c>DeadlinePolicy</c> reached
    /// tier 3 (fail-item) and called <c>api.FailAsync</c>; the worker
    /// journals the same event for the operator. The host journal
    /// reads the four numbers off the payload so the dashboard can
    /// render stall / wall-clock correlation without re-parsing the
    /// timeline.
    /// </summary>
    /// <param name="workItemId">Work item the stall is bound to.</param>
    /// <param name="lastEventAgeMs">Time since the last parsed stream-event, in milliseconds.</param>
    /// <param name="turnElapsedMs">Time since the current cycle's spawn.</param>
    /// <param name="runElapsedMs">Time since the worker process started.</param>
    /// <param name="tier">Escalation tier (3 = fail-item).</param>
    /// <param name="reason">Reason the watchdog attached to the fail-item call.</param>
    public static WorkerEvent ToStallDetectedEvent(
        Guid workItemId,
        long lastEventAgeMs,
        long turnElapsedMs,
        long runElapsedMs,
        int tier,
        string reason)
    {
        return new WorkerEvent
        {
            StallDetected = new StageStallDetected
            {
                WorkItemId = workItemId.ToString(),
                LastEventAgeMs = lastEventAgeMs,
                TurnElapsedMs = turnElapsedMs,
                RunElapsedMs = runElapsedMs,
                Tier = tier,
                Reason = reason,
            },
        };
    }

    /// <summary>
    /// Backpressure drop event (harden-worker-runtime Phase 3, design D4).
    /// The harness events channel dropped a progress-fragment because
    /// the consumer fell behind the producer. Mandatory events never
    /// drop — they wait for the consumer. The host journal maps this
    /// to a <c>worker.events_dropped</c> entry and increments the
    /// <c>events_dropped_total</c> counter.
    /// </summary>
    /// <param name="workItemId">Work item the drop is bound to.</param>
    /// <param name="kind">Drop reason. The open set today is <c>"progress"</c>.</param>
    public static WorkerEvent ToEventsDroppedEvent(Guid workItemId, string kind)
    {
        return new WorkerEvent
        {
            EventsDropped = new StageEventsDropped
            {
                WorkItemId = workItemId.ToString(),
                Kind = kind,
            },
        };
    }
}
