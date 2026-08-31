namespace Comuki.Engine.Orchestration.Domain;

/// <summary>
/// Run status. A run is one goal from intake; its plan is a graph of work items.
/// Transitions are driven by the orchestration handlers (state machine lands
/// with the queue slice).
/// </summary>
public enum RunStatus
{
    Queued = 0,
    Waiting = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Escalated = 6,
}
