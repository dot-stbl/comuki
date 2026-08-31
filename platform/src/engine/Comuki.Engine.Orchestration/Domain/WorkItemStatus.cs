namespace Comuki.Engine.Orchestration.Domain;

/// <summary>
/// Work item status — one profile launch inside a plan. Deliberately has no
/// <c>Stalled</c> member: a stall is an event, and the item moves to
/// <see cref="Failed"/> or back to <see cref="Queued"/> by policy.
/// </summary>
public enum WorkItemStatus
{
    Blocked = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
