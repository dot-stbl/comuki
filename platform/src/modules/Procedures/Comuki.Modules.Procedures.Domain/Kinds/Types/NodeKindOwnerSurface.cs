namespace Comuki.Modules.Procedures.Domain.Kinds.Types;

/// <summary>
/// The closed set of execution surfaces a procedure-node kind binds its
/// instances to: a <c>WorkItem</c> runs under claim/lease (agent work), a
/// <c>Decision</c> blocks on a human (human-gate), a
/// <c>BrokerOperation</c> routes through the capability broker (capability),
/// a <c>BrainOperation</c> runs inside the brain as a typed prompt
/// (classify/plan/review). Wire form is PascalCase so the descriptor
/// frontmatter and the compiled procedure JSON share the same identifier.
/// </summary>
public readonly record struct NodeKindOwnerSurface
{
    private readonly string? value;

    private NodeKindOwnerSurface(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Default placeholder — <c>default(NodeKindOwnerSurface)</c>. Not a
    /// valid owner; a descriptor missing an owner surface lands here and the
    /// catalog refuses it (spec scenario: "Descriptor missing an owner").
    /// </summary>
    public static NodeKindOwnerSurface Unspecified { get; }

    /// <summary>An instance is a work item under lease — the worker spine owns it.</summary>
    public static NodeKindOwnerSurface WorkItem { get; } = new(nameof(WorkItem));

    /// <summary>An instance is a decision — a human (or humans) approves, rejects, or expires it.</summary>
    public static NodeKindOwnerSurface Decision { get; } = new(nameof(Decision));

    /// <summary>An instance is a broker operation — capability surface, gated by exposure class.</summary>
    public static NodeKindOwnerSurface BrokerOperation { get; } = new(nameof(BrokerOperation));

    /// <summary>An instance is a brain operation — runs inside the brain as a typed prompt.</summary>
    public static NodeKindOwnerSurface BrainOperation { get; } = new(nameof(BrainOperation));

    /// <summary>Wire-form string — the PascalCase text the frontmatter and JSON share.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All owner surfaces (excludes <see cref="Unspecified"/>).</summary>
    public static IReadOnlyList<NodeKindOwnerSurface> All { get; } =
        [WorkItem, Decision, BrokerOperation, BrainOperation];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire">The stored PascalCase wire form.</param>
    // TODO(SMART-TYPES): убрать после [SmartType]-генератора — см. source-generators.md §1
    public static NodeKindOwnerSurface FromWire(string wire)
    {
        return wire switch
        {
            nameof(WorkItem) => WorkItem,
            nameof(Decision) => Decision,
            nameof(BrokerOperation) => BrokerOperation,
            nameof(BrainOperation) => BrainOperation,
            _ => throw new ArgumentOutOfRangeException(
                nameof(wire),
                wire,
                $"Unknown {nameof(NodeKindOwnerSurface)} value."),
        };
    }
}
