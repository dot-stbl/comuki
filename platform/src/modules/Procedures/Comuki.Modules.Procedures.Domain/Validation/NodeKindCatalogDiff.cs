namespace Comuki.Modules.Procedures.Domain.Validation;

/// <summary>
/// A snapshot of how a proposed catalog draft differs from a previous
/// catalog version. The validator computes this once per
/// <c>ValidateAsync</c> call to decide which kinds may be retracted and
/// which catalog version to assign next. Pure data — no I/O, no DI.
/// </summary>
/// <param name="AddedKeys">
/// Kind keys present in the new catalog but not in the previous one.
/// Each entry also describes which surface owns the new kind, so the
/// validator can refuse before publishing if the new surface isn't
/// resolvable.
/// </param>
/// <param name="AlteredKeys">
/// Kind keys present in both catalogs, but with at least one wire-contract
/// field changed (outcome ports, parameter schema, evidence requirements,
/// owner surface, idempotency, approval floor). Each entry pairs the key
/// with a non-empty list of which fields changed.
/// </param>
/// <param name="RetractedKeys">
/// Kind keys present in the previous catalog but absent from the new one.
/// The validator consults <see cref="Ports.IProcedureKindUsageLookup"/> for each;
/// a non-empty result refuses the publication.
/// </param>
public sealed record NodeKindCatalogDiff(
    IReadOnlyList<string> AddedKeys,
    IReadOnlyList<string> AlteredKeys,
    IReadOnlyList<string> RetractedKeys)
{
    /// <summary>True when the proposed draft is identical to the previous catalog.</summary>
    public bool IsUnchanged =>
        AddedKeys.Count == 0 && AlteredKeys.Count == 0 && RetractedKeys.Count == 0;

    /// <summary>True when the proposed draft changes the catalog in any way — drives the version bump.</summary>
    public bool HasChanges => !IsUnchanged;
}
