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
}
