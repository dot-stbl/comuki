namespace Comuki.Modules.Work.Domain;

/// <summary>
/// Task lifecycle status — the smart-type over the closed set of
/// states a <see cref="WorkTask"/> visits. Replaces the previous
/// plain-label enum (per <c>smart-types.md</c>): a Task has no
/// <c>(WorkTaskStatus)47</c> escape hatch. Transitions are guarded by
/// <see cref="WorkTaskTransitions"/>.
/// Wire form is the PascalCase member name (<c>Draft</c>,
/// <c>Ready</c>, …) — chosen to round-trip cleanly through EF's
/// <c>HasConversion&lt;string&gt;</c> once Work.Infrastructure lands.
/// </summary>
public readonly record struct WorkTaskStatus
{
    private readonly string? value;

    private WorkTaskStatus(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Default placeholder — <c>default(WorkTaskStatus)</c>. Not a real
    /// status; surfaces only when an EF row predates the column or when
    /// <c>HasConversion</c> hits an unknown string. Cast to a working
    /// status before acting on it.
    /// </summary>
    public static WorkTaskStatus Unspecified { get; }

    /// <summary>Initial state — the Task is created, brief captured, not yet eligible for dispatch.</summary>
    public static WorkTaskStatus Draft { get; } = new("Draft");

    /// <summary>Brief is final, dependencies satisfied, eligible for dispatch.</summary>
    public static WorkTaskStatus Ready { get; } = new("Ready");

    /// <summary>At least one Run attempt is in flight; the Task owns at most one active attempt.</summary>
    public static WorkTaskStatus Active { get; } = new("Active");

    /// <summary>An attempt reached a terminal failure; an explicit Decision is required to advance.</summary>
    public static WorkTaskStatus Blocked { get; } = new("Blocked");

    /// <summary>Terminal — the Task finished with one of the resolution outcomes.</summary>
    public static WorkTaskStatus Resolved { get; } = new("Resolved");

    /// <summary>Terminal — the Task was cancelled from any non-terminal status.</summary>
    public static WorkTaskStatus Cancelled { get; } = new("Cancelled");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<WorkTaskStatus> All { get; } =
        [Draft, Ready, Active, Blocked, Resolved, Cancelled];

    /// <summary>The non-terminal statuses from which a <c>Cancelled</c> transition is legal.</summary>
    public static IReadOnlySet<WorkTaskStatus> Cancellable { get; } =
        new HashSet<WorkTaskStatus> { Draft, Ready, Active, Blocked };

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    public static WorkTaskStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Draft) => Draft,
            nameof(Ready) => Ready,
            nameof(Active) => Active,
            nameof(Blocked) => Blocked,
            nameof(Resolved) => Resolved,
            nameof(Cancelled) => Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(WorkTaskStatus)} value."),
        };
    }
}
