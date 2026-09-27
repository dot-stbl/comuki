namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// The closed set of access levels a Repository attaches to Projects through.
/// Lattice order: <see cref="External"/> is the most restrictive, then
/// <see cref="Read"/>, then <see cref="Write"/>;
/// <see cref="Min(RepositoryAccess, RepositoryAccess)"/> returns the more
/// restrictive value, which is how effective access is resolved
/// (<c>min(attachment.access, credential.DefaultAccess)</c>). Wire form is
/// PascalCase so EF stores the same identifier the JSON contract surfaces.
/// </summary>
public readonly record struct RepositoryAccess
{
    private readonly string? value;

    private RepositoryAccess(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a real access level; rejected by credential default access checks.</summary>
    public static RepositoryAccess Unspecified { get; }

    /// <summary>No credential is resolved; an external change request is the only path forward (spec "External attachment never resolves a credential").</summary>
    public static RepositoryAccess External { get; } = new("External");

    /// <summary>Read-only access; writes are refused even when the credential supports them.</summary>
    public static RepositoryAccess Read { get; } = new("Read");

    /// <summary>Full read-write access; gated by the credential's default access in turn.</summary>
    public static RepositoryAccess Write { get; } = new("Write");

    /// <summary>Wire-form string — the PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <summary>All working access levels — excludes the placeholder <see cref="Unspecified"/>. One entry per PascalCase wire form.</summary>
    public static IReadOnlyList<RepositoryAccess> All { get; } =
        [External, Read, Write];

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// Lattice meet: returns the more restrictive of <paramref name="left"/> and <paramref name="right"/>.
    /// Order: <see cref="External"/> is the most restrictive, then <see cref="Read"/>,
    /// then <see cref="Write"/>; the meet is the lower of the two in that order.
    /// <see cref="Unspecified"/> meets anything into
    /// <see cref="Unspecified"/> — a forgotten default never silently
    /// widens access. Pure — no I/O, no DI, no exceptions.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static RepositoryAccess Min(RepositoryAccess left, RepositoryAccess right)
    {
        return (Rank(left), Rank(right)) switch
        {
            (0, _) or (_, 0) => Unspecified,
            var (l, r) when l <= r => left,
            _ => right,
        };
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire"></param>
    public static RepositoryAccess FromWire(string wire)
    {
        return wire switch
        {
            nameof(External) => External,
            nameof(Read) => Read,
            nameof(Write) => Write,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(RepositoryAccess)} value."),
        };
    }

    private static int Rank(RepositoryAccess access)
    {
        return access.Value switch
        {
            nameof(Unspecified) => 0,
            nameof(External) => 1,
            nameof(Read) => 2,
            nameof(Write) => 3,
            _ => -1,
        };
    }
}
