using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Validation.Helpers;
using Comuki.Modules.Procedures.Domain.Validation.Ports;

namespace Comuki.Modules.Procedures.Domain.Validation;

/// <summary>
/// Deterministic catalog validator: validates a draft, diffs it against the
/// published version, refuses retractions still in use, and assigns the
/// next version number. Pure — identical inputs always produce an
/// identical <see cref="NodeKindCatalog"/>. All heavy lifting lives in
/// <see cref="CatalogValidation"/>; this class only orchestrates the
/// sequence and supplies the usage lookup.
/// </summary>
/// <param name="usageLookup">Answers "is this kind still referenced?" for the retraction guard.</param>
public sealed class CatalogValidator(IProcedureKindUsageLookup usageLookup)
{
    /// <summary>
    /// Validates the draft and produces the next published catalog version.
    /// The caller supplies the source ref (git ref the draft was loaded
    /// from) and the draft entries; this method validates the schema,
    /// computes the diff, guards retractions, and assigns the version.
    /// </summary>
    /// <param name="sourceRef">Git ref the draft was loaded from.</param>
    /// <param name="draftEntries">Proposed entries (file-stem keys + descriptors).</param>
    /// <param name="previous">Currently-published catalog; null on the first publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NodeKindCatalog> ValidateAsync(
        string sourceRef,
        IReadOnlyList<NodeKindCatalogEntry> draftEntries,
        NodeKindCatalog? previous,
        CancellationToken cancellationToken = default)
    {
        CatalogValidation.ValidateSchema(draftEntries);

        var diff = CatalogValidation.ComputeDiff(draftEntries, previous);
        await CatalogValidation.RefuseRetractionsInUseAsync(usageLookup, diff.RetractedKeys, cancellationToken);

        return new NodeKindCatalog(
            previous is null
                ? NodeKindCatalog.BaselineVersion
                : CatalogValidation.ComputeNextVersion(previous.Version, diff),
            sourceRef,
            draftEntries);
    }
}
