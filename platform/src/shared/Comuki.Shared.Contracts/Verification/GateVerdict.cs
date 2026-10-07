namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Closed set of gate verdicts (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "VerificationRecord is a
/// per-WorkItem sibling table"). The wire form is the lowercase value
/// <see cref="Value"/> exposes — <c>"pending"</c> / <c>"passed"</c> /
/// <c>"failed"</c> — the canonical values the spec and the
/// <c>gate.evaluated</c> journal payload agree on. A freshly-loaded
/// row whose column default is the canonical "no verdict" string
/// (<c>"pending"</c>) round-trips through
/// <see cref="FromWire"/> without throwing; any other unknown wire
/// value collapses to <see cref="Unspecified"/>.
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

    /// <summary>
    /// Wire form of <see cref="Unspecified"/> — what
    /// <see cref="Value"/> returns when the smart-type is the named
    /// <c>default</c>. Named after <see cref="Unspecified"/> (per
    /// <c>smart-types.md</c>) so a freshly-loaded row whose wire value
    /// is the canonical "no verdict" string round-trips through
    /// <see cref="Value"/> without falling back to <see cref="string.Empty"/>
    /// (an empty wire value is impossible to distinguish from an
    /// error in the reader).
    /// </summary>
    public const string UnspecifiedValue = "unspecified";

    /// <summary>The wire value this verdict carries — lowercase, the canonical literal the spec &amp; journal payload both use.</summary>
    public string Value => value ?? UnspecifiedValue;

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
