namespace Comuki.Engine.Orchestration.Domain.Journal;

/// <summary>
/// Stable journal event type strings. Open set — worker-reported events carry
/// their own dotted kinds; the constants below are the platform-owned ones.
/// </summary>
public static class RunEventTypes
{
    /// <summary>A run changed status; payload carries from/to and the actor.</summary>
    public const string RunStatusChanged = "run.status_changed";

    /// <summary>A work item changed status; payload carries the item id, from/to and the actor.</summary>
    public const string WorkItemStatusChanged = "work_item.status_changed";

    /// <summary>A worker reported a translated pi event; payload mirrors the pi event.</summary>
    public const string WorkerReported = "worker.reported";

    /// <summary>
    /// The reaper closed an expired lease; payload carries the item id, from/to
    /// (Running -> Queued requeue or Running -> Failed after max attempts) and the attempt count.
    /// </summary>
    public const string WorkItemLeaseExpired = "work_item.lease_expired";

    /// <summary>
    /// Hard project budget exceeded; payload carries spent/hard limit in USD micros
    /// and the project id. Emitted by the host budget gate before cancel.
    /// </summary>
    public const string BudgetExceeded = "budget.exceeded";

    /// <summary>
    /// The escalation-timeout sweeper closed an Escalated run that no human
    /// acted on past the configured window; payload carries the run id,
    /// from/to status and the age in seconds at the moment of the sweep.
    /// </summary>
    public const string RunEscalationTimeout = "run.escalation_timeout";

    /// <summary>
    /// A coding-agent slot passed <c>ISlotAdmissionEvaluator</c> and is
    /// cleared to start; payload carries <c>admissionId</c>, <c>envClass</c>,
    /// <c>profileKey</c>, and <c>isolationClass</c>. Never includes secret
    /// values — <c>SecretRefs</c> ride separately as refs only (worker-admission
    /// spec §"Journal admission").
    /// </summary>
    public const string WorkerAdmitted = "worker.admitted";

    /// <summary>
    /// A coding-agent slot was refused by the admission gate; payload
    /// carries <c>admissionId</c> (the slot's id when present) and the
    /// stable typed <c>denialCode</c> from <c>AdmissionCodes</c>.
    /// Operators branch on the code; the code is the contract.
    /// </summary>
    public const string WorkerAdmissionDenied = "worker.admission_denied";

    /// <summary>
    /// The Translator's <c>VerifyRunner</c> finished a verify-profile
    /// work item; every declared opcode exited zero. Payload carries
    /// <c>{ workItemId, opcodes[] }</c> so the dashboard / journal
    /// reader can see the verify scope without re-parsing
    /// <c>.comuki/environment.toml</c>. <b>Does not</b> mean the item's
    /// <see cref="WorkItemStatusChanged"/> — verify is a side effect that
    /// happens during the run; the run's terminal event is independent.
    /// </summary>
    public const string VerifyCompleted = "verify.completed";

    /// <summary>
    /// The Translator's <c>VerifyRunner</c> failed a verify-profile work
    /// item: an opcode exited non-zero, failed to launch, or hit its
    /// timeout. Payload carries <c>{ workItemId, opcode, exitCode?,
    /// reason }</c>. The Host stays up — a non-zero exit is a verify
    /// report, not a Host crash (isolate-verifier-runtime spec scenario
    /// "Build failure is a verify report").
    /// </summary>
    public const string VerifyFailed = "verify.failed";

    /// <summary>
    /// A sandbox stage flipped a boolean condition (harden-pi-worker-sandbox
    /// 5.1, spec D6): <c>WorkspacePrepared</c>, <c>EgressApplied</c>,
    /// <c>AgentRunning</c>. Payload carries <c>{ name, value }</c>;
    /// conditions are journal events, not columns — the dashboard reads
    /// them off the timeline; the reaper still keys off the lease.
    /// </summary>
    public const string WorkerCondition = "worker.condition";

    /// <summary>
    /// The Translator flushed its accumulated artifact list over the
    /// worker stream just before <c>complete</c> / <c>fail</c>
    /// (harden-pi-worker-sandbox 5.2, spec D7). Payload carries the
    /// drain's <c>{ workItemId, artifacts[] }</c>; the host-side
    /// packager reads this to skip already-bundled prefixes. A drain
    /// failure is logged but never blocks <c>complete</c>.
    /// </summary>
    public const string WorkerDrained = "worker.drained";

    /// <summary>
    /// WS7 (issue #87) durable outbox contract name for a Run reaching a
    /// terminal status (Succeeded or Failed) — per the Integration Event
    /// Contract naming convention (context.aggregate.past-tense.vMajor).
    /// This is the dispatcher-delivered OutboxMessage.Type value, a
    /// separate naming scheme from the journal event-type strings above
    /// (it carries the "orchestration." context prefix and a ".v1" major
    /// version suffix; those do not) — do not confuse the two.
    /// </summary>
    public const string RunTerminatedV1 = "orchestration.run.terminated.v1";

    /// <summary>
    /// The host's <c>POST /api/v1/runs/{runId}/steer</c> handler staged
    /// a follow-up research WorkItem (add-orchestra §1 — Baton,
    /// Phase 1a no-LiveSession path). Payload carries the steer text
    /// verbatim and the new work item's id; a follow-up is a Queued
    /// work item, not a status change, so the timeline gets its own
    /// <c>run_events</c> row instead of a <see cref="WorkItemStatusChanged"/>.
    /// </summary>
    public const string RunSteerFollowUpQueued = "run.steer_followup_queued";
}
