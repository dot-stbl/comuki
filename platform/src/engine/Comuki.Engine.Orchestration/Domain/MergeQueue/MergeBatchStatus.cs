namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Lifecycle status of a merge batch — a coordinated group of
/// merge-queue entries that share a release window. Mirrors the
/// <see cref="MergeQueueStatus"/> shape so a batch's status is the
/// aggregate signal of its entries: a batch is
/// <see cref="InProgress"/> once any entry is claimed and
/// <see cref="Merged"/> only after every entry has landed. Wire form is
/// PascalCase to match the historical <c>HasConversion&lt;string&gt;</c>
/// literals.
/// </summary>
public readonly record struct MergeBatchStatus
{
    private readonly string? value;

    private MergeBatchStatus(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real batch status.</summary>
    public static MergeBatchStatus Unspecified { get; }

    /// <summary>Newly created; no entries have been claimed yet.</summary>
    public static MergeBatchStatus Pending { get; } = new("Pending");

    /// <summary>At least one entry has been claimed by an operator.</summary>
    public static MergeBatchStatus InProgress { get; } = new("InProgress");

    /// <summary>Every entry in the batch has landed on its target.</summary>
    public static MergeBatchStatus Merged { get; } = new("Merged");

    /// <summary>Operator dropped the batch (e.g. release train cancelled).</summary>
    public static MergeBatchStatus Abandoned { get; } = new("Abandoned");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<MergeBatchStatus> All { get; } =
        [Pending, InProgress, Merged, Abandoned];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static MergeBatchStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Pending) => Pending,
            nameof(InProgress) => InProgress,
            nameof(Merged) => Merged,
            nameof(Abandoned) => Abandoned,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(MergeBatchStatus)} value."),
        };
    }
}
