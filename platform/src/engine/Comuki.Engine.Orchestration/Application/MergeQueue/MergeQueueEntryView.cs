using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Read shape returned by the merge-queue service. Mirrors
/// <see cref="MergeQueueEntry"/> but exposes the public view of the
/// timestamps and claim fields. Validation / claim / abandon actions
/// return the post-action view so callers see the new state without a
/// round trip. Wire timestamps as unix ms per time-and-wire-format.
/// </summary>
public sealed record MergeQueueEntryView
{
    /// <summary>Entry id (UUIDv7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Optional cross-project scope; null = release train.</summary>
    public ProjectId? ProjectId { get; init; }

    /// <summary>Source branch name.</summary>
    public required string BranchName { get; init; }

    /// <summary>Full PR URL the dashboard deep-links into.</summary>
    public required string PullRequestUrl { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required MergeQueueStatus Status { get; init; }

    /// <summary>Operator-declared conflict resolution hint.</summary>
    public required ConflictResolution ConflictResolution { get; init; }

    /// <summary>Enqueue timestamp (unix ms).</summary>
    public required long EnqueuedAtUnixMs { get; init; }

    /// <summary>Operator currently driving the merge; null while Pending.</summary>
    public string? ClaimedBy { get; init; }

    /// <summary>Last claim timestamp (unix ms); null while Pending.</summary>
    public long? ClaimedAtUnixMs { get; init; }

    /// <summary>Merge timestamp (unix ms); null while not merged.</summary>
    public long? MergedAtUnixMs { get; init; }

    /// <summary>Abandon timestamp (unix ms); null while not abandoned.</summary>
    public long? AbandonedAtUnixMs { get; init; }

    /// <summary>Reason recorded when the entry was abandoned.</summary>
    public string? AbandonedReason { get; init; }

    /// <summary>Free-text notes attached by the operator.</summary>
    public string? Notes { get; init; }

    /// <summary>Projection from the domain aggregate — wire timestamps as unix ms per time-and-wire-format.md.</summary>
    /// <param name="entry"></param>
    public static MergeQueueEntryView FromEntry(MergeQueueEntry entry)
    {
        return new MergeQueueEntryView
        {
            Id = entry.Id,
            ProjectId = entry.ProjectId,
            BranchName = entry.BranchName,
            PullRequestUrl = entry.PullRequestUrl,
            Status = entry.Status,
            ConflictResolution = entry.ConflictResolution,
            EnqueuedAtUnixMs = entry.EnqueuedAt.ToUnixTimeMilliseconds(),
            ClaimedBy = entry.ClaimedBy,
            ClaimedAtUnixMs = entry.ClaimedAt?.ToUnixTimeMilliseconds(),
            MergedAtUnixMs = entry.MergedAt?.ToUnixTimeMilliseconds(),
            AbandonedAtUnixMs = entry.AbandonedAt?.ToUnixTimeMilliseconds(),
            AbandonedReason = entry.AbandonedReason,
            Notes = entry.Notes,
        };
    }
}
