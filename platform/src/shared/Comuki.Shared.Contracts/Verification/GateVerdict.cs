namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Closed set of gate verdicts (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "VerificationRecord is a
/// per-WorkItem sibling table"). The wire form is the same
/// PascalCase value the existing journal event types use (camelCase
/// down at the JSON layer through <c>JsonNamingPolicy.CamelCase</c>),
/// and the canonical "pending" string survives the column-default
/// round-trip when an evaluator has not run for the (work item,
/// gate) pair yet.
/// </summary>
public readonly record struct GateVerdict
{
    private readonly string? value;

    private GateVerdict(string value)
    {
        this.value = value;
    }

    /// <summary>The default verdict — no evaluator has stamped this gate yet.</summary>
    public static GateVerdict Unspecified { get; }

    /// <summary>Default (no value) — equivalent to <see cref="Unspecified"/> for the nullability contract.</summary>
    public string Value => value ?? string.Empty;

    /// <summary>Gate is in flight; not a terminal verdict.</summary>
    public static GateVerdict Pending { get; } = new("pending");

    /// <summary>Gate evaluator returned a positive verdict.</summary>
    public static GateVerdict Passed { get; } = new("passed");

    /// <summary>Gate evaluator returned a negative verdict.</summary>
    public static GateVerdict Failed { get; } = new("failed");

    /// <summary>True when the verdict is one of the three canonical terminals (passed / failed).</summary>
    public bool IsTerminal => this == Passed || this == Failed;

    /// <summary>
    /// Read-side narrowing for unknown wire values — collapses to
    /// <see cref="Unspecified"/> (the column default) so a freshly-loaded
    /// row never throws on a value the catalogue has not seen yet.
    /// </summary>
    /// <param name="wire">Wire-format verdict string.</param>
    public static GateVerdict FromWire(string? wire)
    {
        return wire switch
        {
            "pending" => Pending,
            "passed" => Passed,
            "failed" => Failed,
            _ => Unspecified,
        };
    }
}
