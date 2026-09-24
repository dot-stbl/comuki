namespace Comuki.Engine.Orchestration.Domain;

/// <summary>
/// Run status — a closed set of values that
/// <see cref="Runs.Run"/>'s lifecycle
/// visits. Replaces the previous plain-label enum per
/// <c>smart-types.md</c>: a run has no <c>(RunStatus)47</c> escape hatch.
/// Transitions are guarded by
/// <see cref="Runs.RunTransitions"/>.
/// Wire form is the PascalCase member name (<c>Queued</c>, <c>Running</c>,
/// …) — preserved across the migration so existing DB rows and
/// <c>HasConversion&lt;string&gt;</c> literals (raw SQL, partial indexes)
/// match byte-for-byte.
/// </summary>
public readonly record struct RunStatus
{
    private readonly string? value;

    private RunStatus(string value)
    {
        this.value = value;
    }

    /// <summary>
    /// Default placeholder — <c>default(RunStatus)</c>. Not a real run status;
    /// surfaces only when an EF row predates the column or when
    /// <c>HasConversion</c> hits an unknown string. Cast to a working
    /// status before acting on it.
    /// </summary>
    public static RunStatus Unspecified { get; }

    /// <summary>Newly admitted; no work has started.</summary>
    public static RunStatus Queued { get; } = new("Queued");

    /// <summary>Waiting on an external dependency (queue slot, scheduler window).</summary>
    public static RunStatus Waiting { get; } = new("Waiting");

    /// <summary>A worker is actively driving the run.</summary>
    public static RunStatus Running { get; } = new("Running");

    /// <summary>Terminal — the run completed and produced a result.</summary>
    public static RunStatus Succeeded { get; } = new("Succeeded");

    /// <summary>Terminal — the run failed; a retry surfaces as a fresh Run attempt rather than transitioning this Run.</summary>
    public static RunStatus Failed { get; } = new("Failed");

    /// <summary>Terminal — an operator cancelled the run.</summary>
    public static RunStatus Cancelled { get; } = new("Cancelled");

    /// <summary>
    /// Raised for human attention by the escalation sweeper; the
    /// passive-autonomy ratchet archives stale rows to <see cref="Cancelled"/>.
    /// </summary>
    public static RunStatus Escalated { get; } = new("Escalated");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working statuses (excludes <see cref="Unspecified"/>). One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<RunStatus> All { get; } =
        [Queued, Waiting, Running, Succeeded, Failed, Cancelled, Escalated];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static RunStatus FromWire(string wire)
    {
        return wire switch
        {
            nameof(Queued) => Queued,
            nameof(Waiting) => Waiting,
            nameof(Running) => Running,
            nameof(Succeeded) => Succeeded,
            nameof(Failed) => Failed,
            nameof(Cancelled) => Cancelled,
            nameof(Escalated) => Escalated,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(RunStatus)} value."),
        };
    }
}
