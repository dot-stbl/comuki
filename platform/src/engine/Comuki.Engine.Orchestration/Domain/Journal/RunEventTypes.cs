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

    /// <summary>
    /// A gate provider stamped a verdict for a work item
    /// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
    /// "gate_evaluated journal event"). Payload carries the gate name,
    /// the verdict, the evidence URIs, the work item id and the
    /// <c>VerificationRecord.Id</c> so a reader can join the
    /// <c>verifications</c> row directly. The event is NOT a run
    /// terminal condition — it sits next to <see cref="VerifyCompleted"/>
    /// / <see cref="VerifyFailed"/>, which the existing Translator
    /// emits on the worker stream, but it owns the Coda layer
    /// (the worker stream's verify events are the agent's per-run
    /// self-report, this is the platform's per-gate record).
    /// </summary>
    public const string GateEvaluated = "gate.evaluated";

    /// <summary>
    /// A <c>MergeQueueEntry</c> / <c>MergeBatch</c> was created with a
    /// <c>RunId</c> reference (add-orchestra §3 — Coda, task 3.6
    /// restore the run link). Payload carries the entry / batch id
    /// and the run id so the verification view can join the run's
    /// gate records to the merge-queue row. Emitted in the same
    /// transaction as the row insert when the platform sets the
    /// reference; a row with a null <c>RunId</c> (cross-project
    /// release trains) does not emit this event.
    /// </summary>
    public const string MergeQueueRunReferenced = "merge_queue.run_referenced";

    /// <summary>
    /// The Translator's <c>WorkerProgressWatchdog</c> detected that
    /// <c>last_event_age</c> exceeded <c>WorkerProgressTimeout</c> on
    /// a running cycle (harden-worker-runtime Phase 1, design D1).
    /// Tier 1 of the escalation chain — log + journal; the harness
    /// is still alive and the lease is still held. Payload carries
    /// <c>{ workItemId, last_event_age_ms, tier }</c> where
    /// <c>tier</c> is 1 (warn) or 2 (gentle-kill).
    /// </summary>
    public const string WorkerStallWarn = "worker.stall_warn";

    /// <summary>
    /// The Translator's <c>WorkerProgressWatchdog</c> /
    /// <c>DeadlinePolicy</c> escalated past gentle-kill and set
    /// <c>ShouldFailItem = true</c> with a typed <c>FailReason</c>
    /// (<c>worker.stall_detected</c>,
    /// <c>worker.turn_budget_exceeded</c>,
    /// <c>worker.run_budget_exceeded</c>) — the pump returns a
    /// <c>PiOutcome.FailedStatus</c> and the loop's existing
    /// <c>api.FailAsync</c> call (in <c>TranslatorLoop</c>) carries
    /// the reason on the wire (harden-worker-runtime Phase 1, design
    /// D1 + D2). Tier 3 of the chain — the item is failed and the
    /// host can re-queue. Payload carries <c>{ workItemId,
    /// last_event_age_ms, turn_elapsed_ms, run_elapsed_ms, tier,
    /// reason }</c> so the operator can correlate progress-stall
    /// against wall-clock breaches.
    /// </summary>
    public const string WorkerStallDetected = "worker.stall_detected";

    /// <summary>
    /// The harness events channel dropped a progress-fragment
    /// (<c>text_delta</c>) because the consumer fell behind the
    /// producer, or the line reader dropped a stdout line longer
    /// than <c>TranslatorOptions.MaxLineLengthBytes</c>
    /// (harden-worker-runtime Phase 3, design D4). The single
    /// mandatory <c>PiEvent</c> shape on the stream-json side
    /// (<c>agent_end</c>) never drops — it waits for the consumer.
    /// The run-level lifecycle events (<c>StageStart</c>,
    /// <c>StageReport</c>) are surfaced over the gRPC stream by
    /// the loop and don't flow through this channel. Payload
    /// carries <c>{ workItemId, kind = "progress" }</c>.
    /// </summary>
    public const string WorkerEventsDropped = "worker.events_dropped";
}
