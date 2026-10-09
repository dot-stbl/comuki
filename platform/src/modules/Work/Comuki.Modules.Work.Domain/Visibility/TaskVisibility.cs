namespace Comuki.Modules.Work.Domain.Visibility;

/// <summary>
/// Task visibility — the smart-type over the closed set of access
/// scopes a <see cref="WorkTask"/> reads under. Wire form is the
/// PascalCase member name. The transition
/// <see cref="Unspecified"/> → <see cref="Project"/> is the factory
/// entry; <see cref="Project"/> → <see cref="Mission"/> is the
/// first Mission attachment (one-way — the
/// <see cref="WorkTask.AttachToMission"/> aggregate guard enforces
/// it; there is no return edge and no transfer between Missions).
/// </summary>
public readonly record struct TaskVisibility
{
    private readonly string? value;

    private TaskVisibility(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — <c>default(TaskVisibility)</c>; not a working visibility.</summary>
    public static TaskVisibility Unspecified { get; }

    /// <summary>Standalone Task — visibility follows the project object policy.</summary>
    public static TaskVisibility Project { get; } = new("Project");

    /// <summary>Mission-attached Task — visibility requires Mission access; the Task and prior / future Runs and artifacts become private to the Mission.</summary>
    public static TaskVisibility Mission { get; } = new("Mission");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    public static TaskVisibility FromWire(string wire)
    {
        return wire switch
        {
            nameof(Project) => Project,
            nameof(Mission) => Mission,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(TaskVisibility)} value."),
        };
    }
}
