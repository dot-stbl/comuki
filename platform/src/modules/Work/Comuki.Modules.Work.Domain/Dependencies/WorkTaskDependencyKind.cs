namespace Comuki.Modules.Work.Domain.Dependencies;

/// <summary>
/// The shape of a Task-to-Task edge. Wire form is the PascalCase
/// member name; EF stores the smart-type via
/// <c>HasConversion&lt;string&gt;</c>.
/// </summary>
public readonly record struct WorkTaskDependencyKind
{
    private readonly string? value;

    private WorkTaskDependencyKind(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — <c>default(WorkTaskDependencyKind)</c>; not a working kind.</summary>
    public static WorkTaskDependencyKind Unspecified { get; }

    /// <summary>A directed "blocks" edge: the dependent Task may not resolve before the prerequisite does.</summary>
    public static WorkTaskDependencyKind Blocks { get; } = new("Blocks");

    /// <summary>A symmetric informational "relates-to" edge; no scheduling effect.</summary>
    public static WorkTaskDependencyKind RelatesTo { get; } = new("RelatesTo");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    public static WorkTaskDependencyKind FromWire(string wire)
    {
        return wire switch
        {
            nameof(Blocks) => Blocks,
            nameof(RelatesTo) => RelatesTo,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(WorkTaskDependencyKind)} value."),
        };
    }
}
