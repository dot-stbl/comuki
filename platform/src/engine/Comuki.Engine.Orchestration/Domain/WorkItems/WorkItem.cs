using Comuki.Engine.Orchestration.Domain.Exceptions;
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

    /// <summary>Execution generation this item was leased under — stamped at claim time from the owning Run's generation. Compared against a caller-presented generation on every heartbeat/complete/fail; a mismatch means the owning Run was cancelled/superseded since claim (see WS5) and the guarded SQL (WorkItemQueueSql) rejects the call as an ownership miss, mirrored here as a pure, DB-free predicate.</summary>
    public int Generation { get; private set; }

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
    /// <exception cref="OrchestrationDomainException">a factory invariant was violated.</exception>
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
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemProfileKeyEmpty,
                "profile key must not be empty");
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemImageEmpty,
                "image must not be empty");
        }

        if (string.IsNullOrWhiteSpace(profilesRef))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemProfilesRefEmpty,
                "profiles ref must not be empty");
        }

        if (string.IsNullOrWhiteSpace(brief))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemBriefEmpty,
                "brief must not be empty");
        }

        if (initialStatus != WorkItemStatus.Queued && initialStatus != WorkItemStatus.Blocked)
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemInitialStatusInvalid,
                $"initial work item status must be {nameof(WorkItemStatus.Queued)} or {nameof(WorkItemStatus.Blocked)}, got {initialStatus}");
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
    /// <exception cref="OrchestrationDomainException">the transition is not in <see cref="WorkItemTransitions"/>.</exception>
    public void TransitionTo(WorkItemStatus to, DateTimeOffset now)
    {
        if (!WorkItemTransitions.IsLegal(Status, to))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.WorkItemIllegalTransition,
                $"illegal work item transition {Status} -> {to}");
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
    /// <param name="generation"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void AssignLease(WorkerId workerId, int generation, DateTimeOffset leaseUntil, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Queued)
        {
            throw new InvalidOperationException($"lease can only be assigned to a queued work item, got {Status}");
        }

        LeasedBy = workerId;
        Generation = generation;
        LeaseUntil = leaseUntil;
        HeartbeatAt = now;
        Attempt += 1;
        TransitionTo(WorkItemStatus.Running, now);
    }

    /// <summary>True when <paramref name="generation"/> — the value a heartbeat/complete/fail caller presents — still matches the generation this item was leased under.</summary>
    /// <param name="generation"></param>
    public bool MatchesGeneration(int generation) => Generation == generation;

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
