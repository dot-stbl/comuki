namespace Comuki.Modules.Procedures.Domain.Editions;

/// <summary>
/// The closed set of editions feature keys the effective edition grants.
/// A kind descriptor MAY carry an editions feature key
/// (<see cref="Kinds.NodeKindDescriptor.EditionsFeatureKey"/>); the
/// compile gate refuses publication when the key is not granted. A
/// kind WITHOUT a feature key is always allowed — the gate only
/// checks kinds that opt into a feature.
/// 
/// <para>
/// Default-deny for paid features: the empty set grants nothing. The
/// compile gate asks <see cref="IsGranted"/> for every kind's key —
/// an empty set returns false, every paid kind refuses. The community
/// edition preset grants an empty set; an enterprise edition grants
/// the empty set plus the paid feature keys it carries.
/// </para>
/// </summary>
public sealed record GrantedFeatureKeys(IReadOnlySet<string> Granted)
{
    /// <summary>Community edition — no paid features granted.</summary>
    public static GrantedFeatureKeys Empty { get; } = new(new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Compose a grant set from the supplied feature keys. Stored as a
    /// case-insensitive-free set because edition keys are dot.case and
    /// call sites must compare them verbatim.
    /// </summary>
    /// <param name="granted">The granted feature keys.</param>
    public static GrantedFeatureKeys Of(params string[] granted)
    {
        return new(new HashSet<string>(granted, StringComparer.Ordinal));
    }

    /// <summary>
    /// True when the supplied feature key is in the granted set. False
    /// for the empty grant set — every paid key refuses under community.
    /// </summary>
    /// <param name="featureKey">The dot.case feature key the gate is asking about.</param>
    public bool IsGranted(string featureKey)
    {
        return Granted.Contains(featureKey);
    }
}
