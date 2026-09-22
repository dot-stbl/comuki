using Comuki.Engine.Orchestration.Domain.MergeQueue;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Read shape returned by the merge-batch service. Mirrors
/// <see cref="MergeBatch"/> but exposes the public view of the
/// timestamps. Wire timestamps as unix ms per time-and-wire-format.
/// </summary>
public sealed record MergeBatchView
{
    /// <summary>Batch id (UUIDv7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Operator-supplied human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Ordered PR URLs the batch ships.</summary>
    public required IReadOnlyList<string> PullRequestUrls { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required MergeBatchStatus Status { get; init; }

    /// <summary>Create timestamp (unix ms).</summary>
    public required long CreatedAtUnixMs { get; init; }

    /// <summary>Merge timestamp (unix ms); null while not merged.</summary>
    public long? MergedAtUnixMs { get; init; }

    /// <summary>Abandon timestamp (unix ms); null while not abandoned.</summary>
    public long? AbandonedAtUnixMs { get; init; }

    /// <summary>Reason recorded when the batch was abandoned.</summary>
    public string? AbandonedReason { get; init; }

    /// <summary>Projection from the domain aggregate.</summary>
    /// <param name="batch"></param>
    public static MergeBatchView FromBatch(MergeBatch batch)
    {
        return new MergeBatchView
        {
            Id = batch.Id,
            Name = batch.Name,
            PullRequestUrls = batch.PullRequestUrls,
            Status = batch.Status,
            CreatedAtUnixMs = batch.CreatedAt.ToUnixTimeMilliseconds(),
            MergedAtUnixMs = batch.MergedAt?.ToUnixTimeMilliseconds(),
            AbandonedAtUnixMs = batch.AbandonedAt?.ToUnixTimeMilliseconds(),
            AbandonedReason = batch.AbandonedReason,
        };
    }
}
