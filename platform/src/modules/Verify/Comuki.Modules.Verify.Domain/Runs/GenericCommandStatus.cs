namespace Comuki.Modules.Verify.Domain.Runs;

/// <summary>
/// Lifecycle of a generic-command verification run — a closed set of
/// values, smart-typed per <c>smart-types.md</c> (mirrors
/// <c>Comuki.Engine.Orchestration.Domain.RunStatus</c>): no
/// <c>(GenericCommandStatus)47</c> escape hatch, behaviour lives on the
/// type instead of a switch copied at every call site. The verifier is
/// pessimistic about ordering: <see cref="Running"/> is non-terminal,
/// <see cref="Green"/> and <see cref="Red"/> are terminal and never
/// transition again. Wire form is the PascalCase member name — stored
/// verbatim by <c>HasConversion&lt;string&gt;</c> and matched by the raw
/// SQL literals in <c>GenericCommandStore.ClaimPendingAsync</c>.
/// </summary>
public readonly record struct GenericCommandStatus
{
    private readonly string? value;

    private GenericCommandStatus(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Default placeholder — <c>default(GenericCommandStatus)</c>. Not a
    /// real run status; surfaces only when an EF row predates the column
    /// or <c>HasConversion</c> hits an unknown string. A freshly-created
    /// <see cref="GenericCommandRun"/> is always stamped
    /// <see cref="Pending"/> explicitly — this default is never observed
    /// on a row created through <see cref="GenericCommandRun.Create"/>.
    /// </summary>
    public static GenericCommandStatus Unspecified { get; }

    /// <summary>Just persisted; the worker has not picked it up yet.</summary>
    public static GenericCommandStatus Pending { get; } = new(nameof(Pending));

    /// <summary>Worker has started <c>Process.Start</c>; stdout/stderr stream into <c>output_log</c>.</summary>
    public static GenericCommandStatus Running { get; } = new(nameof(Running));

    /// <summary>Process exit code matched <c>expected_exit_code</c>.</summary>
    public static GenericCommandStatus Green { get; } = new(nameof(Green));

    /// <summary>Process exited non-zero, did not match <c>expected_exit_code</c>, or never launched.</summary>
    public static GenericCommandStatus Red { get; } = new(nameof(Red));

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>).</summary>
    public static IReadOnlyList<GenericCommandStatus> All { get; } =
        [Pending, Running, Green, Red];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire">The stored PascalCase wire form.</param>
    public static GenericCommandStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Pending) => Pending,
            nameof(Running) => Running,
            nameof(Green) => Green,
            nameof(Red) => Red,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(GenericCommandStatus)} value."),
        };
    }
}
