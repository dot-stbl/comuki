namespace Comuki.Modules.Work.Application.Events;

/// <summary>
/// Stable contract names for WorkTask events. The event-naming rule
/// is <c>work.task.&lt;past-tense&gt;.v&lt;major&gt;</c> (the umbrella's
/// pattern from <c>add-mission-cowork/design.md</c>). The values are
/// the strings written to <c>outbox_messages.type</c> in the Work
/// schema, and they are the same strings the Integration sync-bridge
/// subscribes to in <c>work.task.resolved.v1</c> and
/// <c>work.task.cancelled.v1</c> (task 4.1 of the change).
/// </summary>
public static class WorkTaskEventTypes
{
    /// <summary>Emitted when a WorkTask is first admitted (a Draft Task is created).</summary>
    public const string Created = "work.task.created.v1";

    /// <summary>Emitted when a WorkTask transitions to Ready.</summary>
    public const string Readied = "work.task.readied.v1";

    /// <summary>Emitted when a WorkTask transitions to Active (a Run attempt was dispatched).</summary>
    public const string AttemptRequested = "work.task.attempt-requested.v1";

    /// <summary>Emitted when a WorkTask transitions to Blocked (an attempt exhausted, awaiting a Decision).</summary>
    public const string Blocked = "work.task.blocked.v1";

    /// <summary>Emitted when a WorkTask transitions to Resolved with one of the four outcomes.</summary>
    public const string Resolved = "work.task.resolved.v1";

    /// <summary>Emitted when a WorkTask transitions to Cancelled.</summary>
    public const string Cancelled = "work.task.cancelled.v1";

    /// <summary>
    /// Emitted when the active attempt of a non-terminal Task is
    /// cancelled (the Task stays in its current status — Ready /
    /// Active / Blocked — and may be retried). Distinct from
    /// <see cref="Cancelled"/>: this is a per-attempt "stop the
    /// current Run" event, not a terminal Task state transition.
    /// <see cref="Cancelled"/> is therefore
    /// only ever emitted by the terminal <c>Work.Decide</c>
    /// <c>Cancellation</c> branch; the only consumer of
    /// <c>AttemptCancelled</c> is
    /// <c>WorkAttemptCancelledSubscriber</c> which forwards the
    /// Run id to <c>IWorkCancelPort</c>.
    /// </summary>
    public const string AttemptCancelled = "work.task.attempt-cancelled.v1";
}
