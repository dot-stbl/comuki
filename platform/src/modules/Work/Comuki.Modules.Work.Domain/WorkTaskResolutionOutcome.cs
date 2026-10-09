namespace Comuki.Modules.Work.Domain;

/// <summary>
/// Resolution outcome — the closed set of reasons a
/// <see cref="WorkTask"/> may end in once it reaches
/// <see cref="WorkTaskStatus.Resolved"/>. Set exactly once on the
/// <c>→ Resolved</c> edge and immutable thereafter. A null
/// <c>ResolutionOutcome</c> with a non-null Task means the Task is
/// still in <see cref="WorkTaskStatus.Draft"/> /
/// <see cref="WorkTaskStatus.Ready"/> /
/// <see cref="WorkTaskStatus.Active"/> /
/// <see cref="WorkTaskStatus.Blocked"/> /
/// <see cref="WorkTaskStatus.Cancelled"/>.
/// </summary>
public readonly record struct WorkTaskResolutionOutcome
{
    private readonly string? value;

    private WorkTaskResolutionOutcome(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — <c>default(WorkTaskResolutionOutcome)</c>; not a working outcome.</summary>
    public static WorkTaskResolutionOutcome Unspecified { get; }

    /// <summary>The final attempt succeeded and the evidence contract is satisfied.</summary>
    public static WorkTaskResolutionOutcome Succeeded { get; } = new("Succeeded");

    /// <summary>An authorized human waives further execution; the Task ends in <see cref="WorkTaskStatus.Resolved"/>.</summary>
    public static WorkTaskResolutionOutcome Waived { get; } = new("Waived");

    /// <summary>An authorized Decision replaces the Task with a new one bound to the same inbound id.</summary>
    public static WorkTaskResolutionOutcome Replaced { get; } = new("Replaced");

    /// <summary>No further action is possible; the Task ends unresolved with a recorded <c>Failed</c> outcome.</summary>
    public static WorkTaskResolutionOutcome Failed { get; } = new("Failed");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>True for every non-<see cref="Unspecified"/> outcome.</summary>
    public bool HasValue => value is not null;

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    public static WorkTaskResolutionOutcome FromWire(string wire)
    {
        return wire switch
        {
            nameof(Succeeded) => Succeeded,
            nameof(Waived) => Waived,
            nameof(Replaced) => Replaced,
            nameof(Failed) => Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(WorkTaskResolutionOutcome)} value."),
        };
    }
}
