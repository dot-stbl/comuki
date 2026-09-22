namespace Comuki.Engine.Orchestration.Domain.Runs;

/// <summary>
/// Trust class of a run — the platform's autonomy ratchet. A run starts at
/// <see cref="Supervised"/> (every action reviewed, the default state); the
/// ratchet promotes it to <see cref="Pilot"/> after the operator opts in via
/// a setting on the worker profile, and then to <see cref="Trusted"/> after
/// <c>N</c> consecutive <see cref="RunStatus.Succeeded"/> runs (the threshold
/// is configured in <c>TrustClassRatchetOptions</c>). A failure while
/// <see cref="Trusted"/> demotes the run back to <see cref="Supervised"/>.
/// Mutations are guarded by <see cref="Run.PromoteTo"/> /
/// <see cref="Run.DemoteTo"/>. Wire form is PascalCase to match the
/// historical <c>HasConversion&lt;string&gt;</c> literals; <c>Supervised</c>
/// is also the EF column default.
/// </summary>
public readonly record struct RunTrustClass
{
    private readonly string? value;

    private RunTrustClass(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Default placeholder — also the safe starting ratchet rung.
    /// <c>default(RunTrustClass)</c> resolves to <see cref="Supervised"/>
    /// (not <c>Unspecified</c>) so a freshly-loaded EF row whose value was
    /// not written yet still classifies as supervised-by-default and the
    /// ratchet starts from the conservative rung.
    /// </summary>
    public static RunTrustClass Supervised { get; } = new("Supervised");

    /// <summary>Opted-in <see cref="Supervised"/>; ratchet watches completions.</summary>
    public static RunTrustClass Pilot { get; } = new("Pilot");

    /// <summary>Auto-promoted after enough consecutive successes; failures demote back.</summary>
    public static RunTrustClass Trusted { get; } = new("Trusted");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Supervised);

    /// <summary>All trust classes. <see cref="Supervised"/> is the EF column default and the conservative starting rung.</summary>
    public static IReadOnlyList<RunTrustClass> All { get; } =
        [Supervised, Pilot, Trusted];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static RunTrustClass FromWire(string wire)
    {
        return wire switch
        {
            nameof(Supervised) => Supervised,
            nameof(Pilot) => Pilot,
            nameof(Trusted) => Trusted,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(RunTrustClass)} value."),
        };
    }
}
