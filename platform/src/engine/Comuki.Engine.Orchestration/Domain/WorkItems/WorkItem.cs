using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.WorkItems;

/// <summary>
/// Work item — one profile launch inside a run's plan. Lease fields
/// (<see cref="LeasedBy"/>, <see cref="LeaseUntil"/>, <see cref="HeartbeatAt"/>)
/// are owned by the claim/lease flow (next slice) and are schema-only here.
/// Status transitions are guarded by <see cref="WorkItemTransitions"/>.
/// </summary>
public sealed class WorkItem
{
    internal WorkItem()
    {
    }

    /// <summary>Work item id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>Parent run.</summary>
    public RunId RunId { get; private set; }

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public WorkItemStatus Status { get; private set; }

    /// <summary>Worker profile to launch (e.g. <c>implement</c>, <c>explore-readonly</c>).</summary>
    public string ProfileKey { get; private set; } = string.Empty;

    /// <summary>Brief for the worker, stored as raw JSON (<c>jsonb</c> column).</summary>
    public string Brief { get; private set; } = string.Empty;

    /// <summary>Worker currently holding the lease, if any.</summary>
    public WorkerId? LeasedBy { get; private set; }

    /// <summary>Lease expiry; a stale lease is reaped back to the queue by policy.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    /// <summary>Last worker heartbeat.</summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    /// <summary>When the plan applied this item.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last status change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a work item. The initial status must be <see cref="WorkItemStatus.Queued"/>
    /// (no dependencies) or <see cref="WorkItemStatus.Blocked"/> (has unsatisfied dependencies).
    /// </summary>
    /// <param name="runId"></param>
    /// <param name="profileKey"></param>
    /// <param name="brief"></param>
    /// <param name="initialStatus"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static WorkItem Create(RunId runId, string profileKey, string brief, WorkItemStatus initialStatus, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            throw new ArgumentException("profile key must not be empty", nameof(profileKey));
        }

        if (string.IsNullOrWhiteSpace(brief))
        {
            throw new ArgumentException("brief must not be empty", nameof(brief));
        }

        if (initialStatus is not (WorkItemStatus.Queued or WorkItemStatus.Blocked))
        {
            throw new ArgumentException(
                $"initial work item status must be {nameof(WorkItemStatus.Queued)} or {nameof(WorkItemStatus.Blocked)}, got {initialStatus}",
                nameof(initialStatus));
        }

        var id = Guid.CreateVersion7();
        return new WorkItem
        {
            Id = id,
            RunId = runId,
            ProfileKey = profileKey,
            Brief = brief,
            Status = initialStatus,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Applies a status transition; illegal transitions throw — see <see cref="WorkItemTransitions"/>.</summary>
    /// <param name="to"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void TransitionTo(WorkItemStatus to, DateTimeOffset now)
    {
        if (!WorkItemTransitions.IsLegal(Status, to))
        {
            throw new InvalidOperationException($"illegal work item transition {Status} -> {to}");
        }

        Status = to;
        UpdatedAt = now;
    }
}
