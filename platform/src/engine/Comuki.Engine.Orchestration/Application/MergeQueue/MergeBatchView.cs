using Comuki.Engine.Orchestration.Domain.MergeQueue;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Read shape returned by the merge-batch service. Mirrors
/// <see cref="MergeBatch"/> but exposes the public view of the
/// timestamps. Wire timestamps as unix ms per time-and-wire-format.
/// </summary>
/// <param name="Id"></param>
/// <param name="Name"></param>
/// <param name="PullRequestUrls"></param>
/// <param name="Status"></param>
/// <param name="CreatedAtUnixMs"></param>
/// <param name="MergedAtUnixMs"></param>
/// <param name="AbandonedAtUnixMs"></param>
/// <param name="AbandonedReason"></param>
public sealed record MergeBatchView(
    Guid Id,
    string Name,
    IReadOnlyList<string> PullRequestUrls,
    MergeBatchStatus Status,
    long CreatedAtUnixMs,
    long? MergedAtUnixMs,
    long? AbandonedAtUnixMs,
    string? AbandonedReason)
{
    /// <summary>Projection from the domain aggregate.</summary>
    /// <param name="batch"></param>
    public static MergeBatchView FromBatch(MergeBatch batch)
    {
        return new MergeBatchView(
            batch.Id,
            batch.Name,
            batch.PullRequestUrls,
            batch.Status,
            batch.CreatedAt.ToUnixTimeMilliseconds(),
            batch.MergedAt?.ToUnixTimeMilliseconds(),
            batch.AbandonedAt?.ToUnixTimeMilliseconds(),
            batch.AbandonedReason);
    }
}

/// <summary>Paged list view — items + total in scope.</summary>
/// <param name="Items"></param>
/// <param name="Total"></param>
public sealed record MergeBatchPage(IReadOnlyList<MergeBatchView> Items, int Total);
