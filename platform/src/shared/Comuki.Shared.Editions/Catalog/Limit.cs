using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Catalog;

/// <summary>
/// One entry in the <see cref="Limits"/> registry: a count-quota
/// key with a Community cap and optional per-rank paid overrides. Unlike
/// <see cref="Feature"/>, a limit always applies (Community included) —
/// only its numeric cap changes by tier.
/// </summary>
public sealed record Limit
{
    private static readonly IReadOnlyDictionary<int, int> emptyPaidValues = new Dictionary<int, int>();

    /// <summary>Declares a registry entry. <paramref name="key"/> must be well-formed (<see cref="LimitKey.IsWellFormed"/>); every <paramref name="paidValues"/> rank must be positive (rank 0 is <paramref name="communityValue"/>).</summary>
    public static Limit Define(string key, string description, int communityValue, IReadOnlyDictionary<int, int>? paidValues = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Limit description cannot be empty.", nameof(description));
        }

        foreach (var rank in (paidValues ?? emptyPaidValues).Keys)
        {
            if (rank <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paidValues), rank, "A limit's paid override rank must be positive — rank 0 is CommunityValue.");
            }
        }

        return new Limit(LimitKey.Parse(key), description, communityValue, paidValues ?? emptyPaidValues);
    }

    private Limit(LimitKey key, string description, int communityValue, IReadOnlyDictionary<int, int> paidValues)
    {
        Key = key;
        Description = description;
        CommunityValue = communityValue;
        PaidValues = paidValues;
    }

    /// <summary>The well-formed, unique key.</summary>
    public LimitKey Key { get; }

    /// <summary>Human-readable description.</summary>
    public string Description { get; }

    /// <summary>The Community-tier cap.</summary>
    public int CommunityValue { get; }

    /// <summary>Per-rank overrides above Community; a rank not present here inherits the highest applicable lower rank's value, else <see cref="CommunityValue"/>.</summary>
    public IReadOnlyDictionary<int, int> PaidValues { get; }

    /// <summary>
    /// The effective cap for <paramref name="tier"/>: the highest
    /// <see cref="PaidValues"/> entry whose rank is at or below
    /// <paramref name="tier"/>'s rank, else <see cref="CommunityValue"/>.
    /// </summary>
    public int ValueFor(EditionTier tier)
    {
        var value = CommunityValue;
        var bestRank = 0;
        foreach (var (rank, capValue) in PaidValues)
        {
            if (rank <= tier.Rank && rank >= bestRank)
            {
                bestRank = rank;
                value = capValue;
            }
        }

        return value;
    }
}
