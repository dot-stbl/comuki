namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Merge-batch aggregate — a coordinated group of merge-queue entries
/// that share a release window. The batch owns its name, the ordered
/// list of PR URLs it ships, and the lifecycle timestamps. Status
/// transitions are guarded by <see cref="MergeBatchTransitions"/>; the
/// only mutators are <see cref="Claim"/>, <see cref="MarkMerged"/> and
/// <see cref="MarkAbandoned"/>. The list of PR URLs is owned by the
/// batch; readers query the store for the corresponding
/// <see cref="MergeQueueEntry"/> rows.
/// </summary>
public sealed class MergeBatch
{
    internal MergeBatch()
    {
    }

    /// <summary>Batch id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>Operator-supplied human-readable name; non-empty, bounded.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>PR URLs in the batch — the ordered list the operator declared.</summary>
    public IReadOnlyList<string> PullRequestUrls { get; private set; } = [];

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public MergeBatchStatus Status { get; private set; }

    /// <summary>When the batch was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the batch transitioned to <see cref="MergeBatchStatus.Merged"/>.</summary>
    public DateTimeOffset? MergedAt { get; private set; }

    /// <summary>When the batch transitioned to <see cref="MergeBatchStatus.Abandoned"/>.</summary>
    public DateTimeOffset? AbandonedAt { get; private set; }

    /// <summary>Free-text reason recorded when the batch was abandoned.</summary>
    public string? AbandonedReason { get; private set; }

    /// <summary>
    /// Creates a new batch in <see cref="MergeBatchStatus.Pending"/>. The
    /// name and the PR URL list must be non-empty; PR URLs are stored
    /// as supplied (the engine does not parse them).
    /// </summary>
    /// <param name="name"></param>
    /// <param name="pullRequestUrls"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static MergeBatch Create(
        string name,
        IReadOnlyList<string> pullRequestUrls,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("batch name must not be empty", nameof(name));
        }

        if (pullRequestUrls is null || pullRequestUrls.Count == 0)
        {
            throw new ArgumentException("pull request url list must not be empty", nameof(pullRequestUrls));
        }

        foreach (var url in pullRequestUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("pull request url entries must not be empty", nameof(pullRequestUrls));
            }
        }

        var id = Guid.CreateVersion7();
        return new MergeBatch
        {
            Id = id,
            Name = name,
            PullRequestUrls = pullRequestUrls,
            Status = MergeBatchStatus.Pending,
            CreatedAt = now,
        };
    }

    /// <summary>Applies a status transition; illegal transitions throw — see <see cref="MergeBatchTransitions"/>.</summary>
    /// <param name="to"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void TransitionTo(MergeBatchStatus to)
    {
        if (!MergeBatchTransitions.IsLegal(Status, to))
        {
            throw new InvalidOperationException($"illegal merge-batch transition {Status} -> {to}");
        }

        Status = to;
    }

    /// <summary>
    /// Claims the batch: moves <see cref="MergeBatchStatus.Pending"/> to
    /// <see cref="MergeBatchStatus.InProgress"/>. Triggered when the
    /// operator picks up the first entry from the batch.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public void Claim()
    {
        if (Status != MergeBatchStatus.Pending)
        {
            throw new InvalidOperationException($"claim is only legal from {nameof(MergeBatchStatus.Pending)}, got {Status}");
        }

        TransitionTo(MergeBatchStatus.InProgress);
    }

    /// <summary>
    /// Marks the batch as merged. Only legal from
    /// <see cref="MergeBatchStatus.InProgress"/>; sets the merge
    /// timestamp.
    /// </summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void MarkMerged(DateTimeOffset now)
    {
        if (Status != MergeBatchStatus.InProgress)
        {
            throw new InvalidOperationException($"merge is only legal from {nameof(MergeBatchStatus.InProgress)}, got {Status}");
        }

        TransitionTo(MergeBatchStatus.Merged);
        MergedAt = now;
    }

    /// <summary>
    /// Marks the batch as abandoned. Legal from
    /// <see cref="MergeBatchStatus.Pending"/> or
    /// <see cref="MergeBatchStatus.InProgress"/>; records the reason and
    /// the timestamp.
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

        if (Status is not (MergeBatchStatus.Pending or MergeBatchStatus.InProgress))
        {
            throw new InvalidOperationException($"abandon is only legal from {nameof(MergeBatchStatus.Pending)} or {nameof(MergeBatchStatus.InProgress)}, got {Status}");
        }

        TransitionTo(MergeBatchStatus.Abandoned);
        AbandonedAt = now;
        AbandonedReason = reason;
    }

    /// <summary>
    /// Internal reconstitute for the EF store. Not part of the public
    /// domain API; the store is the only caller.
    /// </summary>
    internal static MergeBatch Reconstitute(
        Guid id,
        string name,
        IReadOnlyList<string> pullRequestUrls,
        MergeBatchStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? mergedAt,
        DateTimeOffset? abandonedAt,
        string? abandonedReason)
    {
        return new MergeBatch
        {
            Id = id,
            Name = name,
            PullRequestUrls = pullRequestUrls,
            Status = status,
            CreatedAt = createdAt,
            MergedAt = mergedAt,
            AbandonedAt = abandonedAt,
            AbandonedReason = abandonedReason,
        };
    }
}
