using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Read shape returned by the merge-queue service. Mirrors
/// <see cref="MergeQueueEntry"/> but exposes the public view of the
/// timestamps and claim fields. Validation / claim / abandon actions
/// return the post-action view so callers see the new state without a
/// round trip.
/// </summary>
/// <param name="Id"></param>
/// <param name="ProjectId"></param>
/// <param name="BranchName"></param>
/// <param name="PullRequestUrl"></param>
/// <param name="Status"></param>
/// <param name="ConflictResolution"></param>
/// <param name="EnqueuedAtUnixMs"></param>
/// <param name="ClaimedBy"></param>
/// <param name="ClaimedAtUnixMs"></param>
/// <param name="MergedAtUnixMs"></param>
/// <param name="AbandonedAtUnixMs"></param>
/// <param name="AbandonedReason"></param>
/// <param name="Notes"></param>
public sealed record MergeQueueEntryView(
    Guid Id,
    ProjectId? ProjectId,
    string BranchName,
    string PullRequestUrl,
    MergeQueueStatus Status,
    ConflictResolution ConflictResolution,
    long EnqueuedAtUnixMs,
    string? ClaimedBy,
    long? ClaimedAtUnixMs,
    long? MergedAtUnixMs,
    long? AbandonedAtUnixMs,
    string? AbandonedReason,
    string? Notes)
{
    /// <summary>Projection from the domain aggregate — wire timestamps as unix ms per time-and-wire-format.md.</summary>
    /// <param name="entry"></param>
    public static MergeQueueEntryView FromEntry(MergeQueueEntry entry)
    {
        return new MergeQueueEntryView(
            entry.Id,
            entry.ProjectId,
            entry.BranchName,
            entry.PullRequestUrl,
            entry.Status,
            entry.ConflictResolution,
            entry.EnqueuedAt.ToUnixTimeMilliseconds(),
            entry.ClaimedBy,
            entry.ClaimedAt?.ToUnixTimeMilliseconds(),
            entry.MergedAt?.ToUnixTimeMilliseconds(),
            entry.AbandonedAt?.ToUnixTimeMilliseconds(),
            entry.AbandonedReason,
            entry.Notes);
    }
}

/// <summary>Paged list view — items + total in scope.</summary>
/// <param name="Items"></param>
/// <param name="Total"></param>
public sealed record MergeQueuePage(IReadOnlyList<MergeQueueEntryView> Items, int Total);
