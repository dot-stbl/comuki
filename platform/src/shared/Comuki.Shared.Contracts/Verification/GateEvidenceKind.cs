namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Closed set of evidence kinds the verification view reads (add-orchestra
/// §3 — Coda, <c>verification/spec.md</c> Requirement "Bundle accepts
/// text/x-diff evidence"). The wire form is the dotted kind name; new
/// kinds are added by the platform when a bundle member type needs to
/// be enumerated — providers MAY emit kinds outside this set, and
/// <see cref="FromWire"/> collapses unknown values
/// to <see cref="Other"/>.
/// </summary>
public readonly record struct GateEvidenceKind
{
    private readonly string? value;

    private GateEvidenceKind(string value)
    {
        this.value = value;
    }

    /// <summary>The default kind — a literal whose value is not one of the named kinds.</summary>
    public static GateEvidenceKind Unspecified { get; }

    /// <summary>Kind value (lowercase, dot.case).</summary>
    public string Value => value ?? string.Empty;

    /// <summary>The <c>changeset.diff</c> bundle member — a unified text/x-diff the gate consults.</summary>
    public static GateEvidenceKind Cmdiff { get; } = new("cmdiff");

    /// <summary>The captured stdout of the gate's underlying command.</summary>
    public static GateEvidenceKind Stdout { get; } = new("stdout");

    /// <summary>The captured stderr of the gate's underlying command.</summary>
    public static GateEvidenceKind Stderr { get; } = new("stderr");

    /// <summary>A typed JSON document the gate produced as evidence.</summary>
    public static GateEvidenceKind Json { get; } = new("json");

    /// <summary>Unknown / not on the named list — wire value preserved for round-trip.</summary>
    public static GateEvidenceKind Other { get; } = new("other");

    /// <summary>
    /// Read-side narrowing — collapses unknown wire values to
    /// <see cref="Other"/> so the view can render them without throwing.
    /// </summary>
    /// <param name="wire">Wire-format kind string.</param>
    public static GateEvidenceKind FromWire(string? wire)
    {
        return wire switch
        {
            "cmdiff" => Cmdiff,
            "stdout" => Stdout,
            "stderr" => Stderr,
            "json" => Json,
            "other" => Other,
            _ => Other,
        };
    }
}
