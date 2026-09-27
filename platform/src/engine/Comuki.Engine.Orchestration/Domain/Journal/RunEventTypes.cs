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
    /// WS7 (issue #87) durable outbox contract name for a Run reaching a
    /// terminal status (Succeeded or Failed) — per the Integration Event
    /// Contract naming convention (context.aggregate.past-tense.vMajor).
    /// This is the dispatcher-delivered OutboxMessage.Type value, a
    /// separate naming scheme from the journal event-type strings above
    /// (it carries the "orchestration." context prefix and a ".v1" major
    /// version suffix; those do not) — do not confuse the two.
    /// </summary>
    public const string RunTerminatedV1 = "orchestration.run.terminated.v1";
}
