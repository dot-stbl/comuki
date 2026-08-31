using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.WorkItems;

/// <summary>
/// Work item — one profile launch inside a run's plan. The claim labels
/// (<see cref="Image"/>, <see cref="ProfilesRef"/>, <see cref="ProfileKey"/>) are
/// what a worker matches on when claiming; the lease fields
/// (<see cref="LeasedBy"/>, <see cref="LeaseUntil"/>, <see cref="HeartbeatAt"/>)
/// are mutated only through <see cref="AssignLease"/>, <see cref="Heartbeat"/> and
/// <see cref="ReleaseLease"/> (or the guarded SQL of the queue implementation).
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

    /// <summary>Worker image (with digest) the item must run on — claim label.</summary>
    public string Image { get; private set; } = string.Empty;

    /// <summary>Pinned git ref of the profiles repo — claim label.</summary>
    public string ProfilesRef { get; private set; } = string.Empty;

    /// <summary>Brief for the worker, stored as raw JSON (<c>jsonb</c> column).</summary>
    public string Brief { get; private set; } = string.Empty;

    /// <summary>Worker currently holding the lease, if any.</summary>
    public WorkerId? LeasedBy { get; private set; }

    /// <summary>Lease expiry; a stale lease is reaped back to the queue by policy.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    /// <summary>Last worker heartbeat.</summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    /// <summary>How many times the item has been claimed (requeue retries included).</summary>
    public int Attempt { get; private set; }

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
    /// <param name="image"></param>
    /// <param name="profilesRef"></param>
    /// <param name="brief"></param>
    /// <param name="initialStatus"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static WorkItem Create(
        RunId runId,
        string profileKey,
        string image,
        string profilesRef,
        string brief,
        WorkItemStatus initialStatus,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            throw new ArgumentException("profile key must not be empty", nameof(profileKey));
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException("image must not be empty", nameof(image));
        }

        if (string.IsNullOrWhiteSpace(profilesRef))
        {
            throw new ArgumentException("profiles ref must not be empty", nameof(profilesRef));
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
            Image = image,
            ProfilesRef = profilesRef,
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

    /// <summary>
    /// Claims the item: only legal from <see cref="WorkItemStatus.Queued"/> — moves to
    /// <see cref="WorkItemStatus.Running"/>, stamps the lease and bumps
    /// <see cref="Attempt"/>. Mirrors the guarded SQL the EF queue claim runs.
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void AssignLease(WorkerId workerId, DateTimeOffset leaseUntil, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Queued)
        {
            throw new InvalidOperationException($"lease can only be assigned to a queued work item, got {Status}");
        }

        LeasedBy = workerId;
        LeaseUntil = leaseUntil;
        HeartbeatAt = now;
        Attempt += 1;
        TransitionTo(WorkItemStatus.Running, now);
    }

    /// <summary>
    /// Extends the lease of a running, leased item. The owner check lives in the
    /// queue implementation (guarded SQL); here the invariant is status + lease presence.
    /// </summary>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void Heartbeat(DateTimeOffset leaseUntil, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Running || LeasedBy is null)
        {
            throw new InvalidOperationException($"heartbeat requires a running, leased work item, got {Status}");
        }

        LeaseUntil = leaseUntil;
        HeartbeatAt = now;
    }

    /// <summary>
    /// Releases an expired/stalled lease and requeues the item (Running -> Queued),
    /// clearing all lease columns. The reaper decides between this and
    /// <see cref="TransitionTo"/>(<see cref="WorkItemStatus.Failed"/>) via the
    /// max-attempts policy.
    /// </summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void ReleaseLease(DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Running)
        {
            throw new InvalidOperationException($"only a running work item can be released, got {Status}");
        }

        LeasedBy = null;
        LeaseUntil = null;
        HeartbeatAt = null;
        TransitionTo(WorkItemStatus.Queued, now);
    }
}
