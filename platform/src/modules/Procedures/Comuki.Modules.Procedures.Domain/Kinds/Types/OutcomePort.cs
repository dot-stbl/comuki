namespace Comuki.Modules.Procedures.Domain.Kinds.Types;

/// <summary>
/// A typed outcome port a kind declares: a name (the wire form carried in
/// compiled plans and DAG edges) and a short role describing the meaning
/// (pass/fail, approved/rejected, …). Edges connect ports by name; the
/// compile gate checks every port is wired or defaulted (spec §6 "port
/// outcomes are the only conditions"). The smart-type carries a
/// <see cref="Unspecified"/> default so a parsed blank port surfaces as a
/// loud refusal rather than a silent <c>null</c>.
/// </summary>
public readonly record struct OutcomePort
{
    private readonly string? name;

    private OutcomePort(string name, string role)
    {
        this.name = name;
        Role = role;
    }

    /// <summary>Default placeholder — <c>default(OutcomePort)</c>; never a wired port.</summary>
    public static OutcomePort Unspecified { get; }

    /// <summary>One-line description of what it means to follow this port; used by Studio and the compile diagnostic.</summary>
    public string Role { get; }

    /// <summary>Wire-form port name (kebab / dot-cased, stable).</summary>
    public string Name => name ?? "<unspecified>";

    /// <summary>Construct a port with a name and a role. Name must be non-empty.</summary>
    public static OutcomePort Define(string name, string role)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            // boundary: typed-reflective input from frontmatter parsing; a
            // blank port name is a malformed descriptor, not a contract
            // violation we can communicate through nullable.
            throw new ArgumentException("Outcome port name must be non-empty.", nameof(name));
        }

        return new OutcomePort(name.Trim(), role ?? string.Empty);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name;
    }
}
