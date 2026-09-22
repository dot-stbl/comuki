namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Lifecycle status of a single merge-queue entry. Mirrors the
/// <see cref="WorkItemStatus"/> shape but is independent — the
/// merge-queue is an operator-facing coordination tool, not a worker
/// lease, and its transitions are gated by the table in
/// <see cref="MergeQueueTransitions"/>. Wire form is PascalCase to match
/// the historical <c>HasConversion&lt;string&gt;</c> literals.
/// </summary>
public readonly record struct MergeQueueStatus
{
    private readonly string? value;

    private MergeQueueStatus(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real entry status.</summary>
    public static MergeQueueStatus Unspecified { get; }

    /// <summary>Newly enqueued; no operator has picked it up.</summary>
    public static MergeQueueStatus Pending { get; } = new("Pending");

    /// <summary>Operator has claimed the entry and is driving the merge.</summary>
    public static MergeQueueStatus InProgress { get; } = new("InProgress");

    /// <summary>Merge completed; the entry is a historical record only.</summary>
    public static MergeQueueStatus Merged { get; } = new("Merged");

    /// <summary>Operator marked the entry as dropped (rejected, retracted, superseded).</summary>
    public static MergeQueueStatus Abandoned { get; } = new("Abandoned");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<MergeQueueStatus> All { get; } =
        [Pending, InProgress, Merged, Abandoned];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static MergeQueueStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Pending) => Pending,
            nameof(InProgress) => InProgress,
            nameof(Merged) => Merged,
            nameof(Abandoned) => Abandoned,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(MergeQueueStatus)} value."),
        };
    }
}
