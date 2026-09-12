using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Merge-queue entry — one branch / pull-request awaiting coordinated
/// merge into its target. The entry is the unit of state: it carries the
/// PR URL, the operator that claimed it, the conflict-resolution hint
/// and the lifecycle timestamps. Status transitions are guarded by
/// <see cref="MergeQueueTransitions"/>; the only mutators on the
/// aggregate are <see cref="Claim"/>, <see cref="Release"/>,
/// <see cref="MarkMerged"/> and <see cref="MarkAbandoned"/>.
/// </summary>
public sealed class MergeQueueEntry
{
    internal MergeQueueEntry()
    {
    }

    /// <summary>Entry id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Project scope the entry belongs to. Null for cross-project
    /// merges that are not anchored to a single project (release train
    /// scenarios); the scope query filter treats null as a system-wide
    /// row.
    /// </summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>Branch the entry will land; e.g. <c>feature/merge-queue</c>.</summary>
    public string BranchName { get; private set; } = string.Empty;

    /// <summary>
    /// Full pull-request URL the dashboard deep-links into. A free-form
    /// string — the engine does not parse it.
    /// </summary>
    public string PullRequestUrl { get; private set; } = string.Empty;

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public MergeQueueStatus Status { get; private set; }

    /// <summary>Operator-declared conflict resolution hint.</summary>
    public ConflictResolution ConflictResolution { get; private set; }

    /// <summary>When the entry was admitted into the queue.</summary>
    public DateTimeOffset EnqueuedAt { get; private set; }

    /// <summary>Operator currently driving the merge; null while <see cref="Status"/> is <see cref="MergeQueueStatus.Pending"/>.</summary>
    public string? ClaimedBy { get; private set; }

    /// <summary>Last claim timestamp; null while <see cref="Status"/> is <see cref="MergeQueueStatus.Pending"/>.</summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>When the entry transitioned to <see cref="MergeQueueStatus.Merged"/>.</summary>
    public DateTimeOffset? MergedAt { get; private set; }

    /// <summary>When the entry transitioned to <see cref="MergeQueueStatus.Abandoned"/>.</summary>
    public DateTimeOffset? AbandonedAt { get; private set; }

    /// <summary>Free-text reason recorded when the entry was abandoned.</summary>
    public string? AbandonedReason { get; private set; }

    /// <summary>Free-text notes operator may attach to the entry.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Creates a new entry in <see cref="MergeQueueStatus.Pending"/>. The
    /// branch and PR URL must be non-empty; the project id is optional
    /// (cross-project merges).
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="branchName"></param>
    /// <param name="pullRequestUrl"></param>
    /// <param name="conflictResolution"></param>
    /// <param name="notes"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static MergeQueueEntry Create(
        ProjectId? projectId,
        string branchName,
        string pullRequestUrl,
        ConflictResolution conflictResolution,
        string? notes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("branch name must not be empty", nameof(branchName));
        }

        if (string.IsNullOrWhiteSpace(pullRequestUrl))
        {
            throw new ArgumentException("pull request url must not be empty", nameof(pullRequestUrl));
        }

        var id = Guid.CreateVersion7();
        return new MergeQueueEntry
        {
            Id = id,
            ProjectId = projectId,
            BranchName = branchName,
            PullRequestUrl = pullRequestUrl,
            Status = MergeQueueStatus.Pending,
            ConflictResolution = conflictResolution,
            EnqueuedAt = now,
            Notes = notes,
        };
    }

    /// <summary>Applies a status transition; illegal transitions throw — see <see cref="MergeQueueTransitions"/>.</summary>
    /// <param name="to"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void TransitionTo(MergeQueueStatus to)
    {
        if (!MergeQueueTransitions.IsLegal(Status, to))
        {
            throw new InvalidOperationException($"illegal merge-queue transition {Status} -> {to}");
        }

        Status = to;
    }

    /// <summary>
    /// Claims the entry for an operator: only legal from
    /// <see cref="MergeQueueStatus.Pending"/> — moves to
    /// <see cref="MergeQueueStatus.InProgress"/> and stamps the operator
    /// + claim timestamp. Mirrors the invariant the queue store will
    /// re-check before persisting.
    /// </summary>
    /// <param name="operatorId"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void Claim(string operatorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ArgumentException("operator id must not be empty", nameof(operatorId));
        }

        if (Status != MergeQueueStatus.Pending)
        {
            throw new InvalidOperationException($"claim is only legal from {nameof(MergeQueueStatus.Pending)}, got {Status}");
        }

        TransitionTo(MergeQueueStatus.InProgress);
        ClaimedBy = operatorId;
        ClaimedAt = now;
    }

    /// <summary>
    /// Releases an in-progress claim back to <see cref="MergeQueueStatus.Pending"/>,
    /// clearing the claim fields. Used when an operator steps away
    /// without completing the merge.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public void Release()
    {
        if (Status != MergeQueueStatus.InProgress)
        {
            throw new InvalidOperationException($"release is only legal from {nameof(MergeQueueStatus.InProgress)}, got {Status}");
        }

        TransitionTo(MergeQueueStatus.Pending);
        ClaimedBy = null;
        ClaimedAt = null;
    }

    /// <summary>
    /// Marks the entry as merged. Only legal from
    /// <see cref="MergeQueueStatus.InProgress"/>; sets the merge
    /// timestamp and clears the claim fields.
    /// </summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void MarkMerged(DateTimeOffset now)
    {
        if (Status != MergeQueueStatus.InProgress)
        {
            throw new InvalidOperationException($"merge is only legal from {nameof(MergeQueueStatus.InProgress)}, got {Status}");
        }

        TransitionTo(MergeQueueStatus.Merged);
        ClaimedBy = null;
        ClaimedAt = null;
        MergedAt = now;
    }

    /// <summary>
    /// Marks the entry as abandoned. Legal from
    /// <see cref="MergeQueueStatus.Pending"/> or
    /// <see cref="MergeQueueStatus.InProgress"/>; records the reason
    /// and the timestamp.
    /// </summary>
    /// <param name="reason"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public void MarkAbandoned(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("abandon reason must not be empty", nameof(reason));
        }

        if (Status is not (MergeQueueStatus.Pending or MergeQueueStatus.InProgress))
        {
            throw new InvalidOperationException($"abandon is only legal from {nameof(MergeQueueStatus.Pending)} or {nameof(MergeQueueStatus.InProgress)}, got {Status}");
        }

        TransitionTo(MergeQueueStatus.Abandoned);
        ClaimedBy = null;
        ClaimedAt = null;
        AbandonedAt = now;
        AbandonedReason = reason;
    }

    /// <summary>Updates free-text notes. Any status.</summary>
    /// <param name="notes"></param>
    public void SetNotes(string? notes)
    {
        Notes = notes;
    }

    /// <summary>
    /// Internal reconstitute for the raw-SQL <c>RETURNING</c> path in
    /// <c>MergeQueueStoreEf.ClaimNextAsync</c>: the only caller that
    /// needs to assign the private-set fields from a database row.
    /// Not part of the public domain API.
    /// </summary>
    internal static MergeQueueEntry Reconstitute(
        Guid id,
        ProjectId? projectId,
        string branchName,
        string pullRequestUrl,
        MergeQueueStatus status,
        ConflictResolution conflictResolution,
        DateTimeOffset enqueuedAt,
        string? claimedBy,
        DateTimeOffset? claimedAt,
        DateTimeOffset? mergedAt,
        DateTimeOffset? abandonedAt,
        string? abandonedReason,
        string? notes)
    {
        return new MergeQueueEntry
        {
            Id = id,
            ProjectId = projectId,
            BranchName = branchName,
            PullRequestUrl = pullRequestUrl,
            Status = status,
            ConflictResolution = conflictResolution,
            EnqueuedAt = enqueuedAt,
            ClaimedBy = claimedBy,
            ClaimedAt = claimedAt,
            MergedAt = mergedAt,
            AbandonedAt = abandonedAt,
            AbandonedReason = abandonedReason,
            Notes = notes,
        };
    }
}
