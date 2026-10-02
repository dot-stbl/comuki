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
}
