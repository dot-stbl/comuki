namespace Comuki.Shared.Editions.Tiers;

/// <summary>
/// An edition tier: an ordinal <see cref="Rank"/> the capability registry
/// compares coverage against, plus an open string <see cref="Code"/> a
/// license names. Community is fixed at rank 0; every paid tier is added
/// by appending a row to <see cref="EditionTiers"/> — never a closed enum
/// (issue #164 E2).
/// <para>
/// This is a value object with open fields (E2/OQ3 explicitly forbid a
/// closed enum here), not the closed-set discriminator <c>smart-types.md</c>
/// §2 describes — so it keeps a public constructor rather than a private
/// one with named static instances. The one hazard that pattern's naming
/// convention exists to close still applies here: <c>default(EditionTier)</c>
/// bypasses the constructor (structs always have one), so it compiles as
/// <c>Rank == 0</c> (colliding with <see cref="Community"/>) but
/// <c>Code == null</c> — never construct or compare against it; always go
/// through <see cref="Community"/> or <see cref="EditionTiers"/>.
/// </para>
/// </summary>
public readonly record struct EditionTier
{
    /// <summary>The default tier: rank 0, no license required.</summary>
    public static readonly EditionTier Community = new(0, "community");

    /// <summary>Creates a tier. <paramref name="rank"/> must be non-negative; <paramref name="code"/> must be non-empty.</summary>
    public EditionTier(int rank, string code)
    {
        if (rank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Edition tier rank cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Edition tier code cannot be empty.", nameof(code));
        }

        Rank = rank;
        Code = code;
    }

    /// <summary>Ordinal rank; 0 = Community, 1..N = paid, higher = more capable.</summary>
    public int Rank { get; }

    /// <summary>Stable, open string code (<c>"community"</c>, <c>"team"</c>, …) a license names in its payload.</summary>
    public string Code { get; }
}
