namespace Comuki.Engine.Orchestration.Domain;

/// <summary>
/// Work item status — one profile launch inside a plan. Deliberately has no
/// <c>Stalled</c> member: a stall is an event, and the item moves to
/// <see cref="Failed"/> or back to <see cref="Queued"/> by policy.
/// Wire form is PascalCase to match the historical
/// <c>HasConversion&lt;string&gt;</c> literals and the partial indexes the
/// EF configuration composes.
/// </summary>
public readonly record struct WorkItemStatus
{
    private readonly string? value;

    private WorkItemStatus(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real work-item status.</summary>
    public static WorkItemStatus Unspecified { get; }

    /// <summary>Has unsatisfied dependencies; nothing to claim yet.</summary>
    public static WorkItemStatus Blocked { get; } = new("Blocked");

    /// <summary>Newly admitted; the queue is free to claim.</summary>
    public static WorkItemStatus Queued { get; } = new("Queued");

    /// <summary>A worker holds the lease.</summary>
    public static WorkItemStatus Running { get; } = new("Running");

    /// <summary>Terminal — the worker reported success.</summary>
    public static WorkItemStatus Succeeded { get; } = new("Succeeded");

    /// <summary>Terminal — the worker reported failure or the reaper exhausted attempts.</summary>
    public static WorkItemStatus Failed { get; } = new("Failed");

    /// <summary>Terminal — an operator cancelled the item.</summary>
    public static WorkItemStatus Cancelled { get; } = new("Cancelled");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<WorkItemStatus> All { get; } =
        [Blocked, Queued, Running, Succeeded, Failed, Cancelled];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static WorkItemStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Blocked) => Blocked,
            nameof(Queued) => Queued,
            nameof(Running) => Running,
            nameof(Succeeded) => Succeeded,
            nameof(Failed) => Failed,
            nameof(Cancelled) => Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(WorkItemStatus)} value."),
        };
    }
}
