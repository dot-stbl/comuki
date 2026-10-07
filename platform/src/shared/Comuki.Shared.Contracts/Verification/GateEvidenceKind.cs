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

    /// <summary>The default kind — a literal whose value is not one of the named kinds; equivalent to <see cref="Other"/> for the round-tripping contract.</summary>
    public static GateEvidenceKind Unspecified { get; }

    /// <summary>
    /// Wire form of <see cref="Unspecified"/> — what
    /// <see cref="Value"/> returns when the smart-type is the named
    /// <c>default</c>. Per <c>smart-types.md</c>: a freshly-loaded
    /// row's <c>kind</c> never reads back as an empty string; the named
    /// default carries a stable, lowercase wire form (matching the
    /// <see cref="Other"/> value the journal's open-type rule maps
    /// unknown provider-specific kinds onto).
    /// </summary>
    public const string UnspecifiedValue = "other";

    /// <summary>The wire value this kind carries — lowercase, dot.case; the canonical literal the <c>evidence.kind</c> JSON field reads and writes.</summary>
    public string Value => value ?? UnspecifiedValue;

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
