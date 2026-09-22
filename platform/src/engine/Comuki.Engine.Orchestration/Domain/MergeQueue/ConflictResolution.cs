namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// How an operator intends to resolve conflicts when the branch lands.
/// Persisted as a hint alongside the entry so the dashboard can group
/// in-progress merges by strategy; the actual conflict detection +
/// resolution lives outside the engine (see issue #11 conflict-detection
/// follow-up) and this slice only records the operator's declared mode.
/// Wire form is PascalCase to match the historical
/// <c>HasConversion&lt;string&gt;</c> literals.
/// </summary>
public readonly record struct ConflictResolution
{
    private readonly string? value;

    private ConflictResolution(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real strategy.</summary>
    public static ConflictResolution Unspecified { get; }

    /// <summary>No strategy declared yet.</summary>
    public static ConflictResolution None { get; } = new("None");

    /// <summary>Rebase the branch onto the target before merging.</summary>
    public static ConflictResolution AutoRebase { get; } = new("AutoRebase");

    /// <summary>Operator will resolve conflicts by hand on the worktree.</summary>
    public static ConflictResolution Manual { get; } = new("Manual");

    /// <summary>Squash the branch into a single commit on the target.</summary>
    public static ConflictResolution Squash { get; } = new("Squash");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working conflict-resolution strategies (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<ConflictResolution> All { get; } =
        [None, AutoRebase, Manual, Squash];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static ConflictResolution FromWire(string wire)
    {
        return wire switch
        {
            nameof(None) => None,
            nameof(AutoRebase) => AutoRebase,
            nameof(Manual) => Manual,
            nameof(Squash) => Squash,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(ConflictResolution)} value."),
        };
    }
}
