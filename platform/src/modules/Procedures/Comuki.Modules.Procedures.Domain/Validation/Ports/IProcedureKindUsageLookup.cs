namespace Comuki.Modules.Procedures.Domain.Validation.Ports;

/// <summary>
/// Port the catalog validation consults to decide whether a kind may be
/// retracted. The procedure-side implementation lives in the Procedures
/// application layer (a later task); this interface is the seam — the
/// catalog validator never reaches into another module directly.
/// </summary>
public interface IProcedureKindUsageLookup
{
    /// <summary>
    /// Returns every published procedure that references
    /// <paramref name="kindKey"/>. Empty list when the kind is not in use;
    /// the validator then allows retraction. Non-empty triggers a refusal
    /// that names each referencing procedure (spec scenario: "Retraction
    /// blocked by reference").
    /// </summary>
    /// <param name="kindKey">Stable kind identifier being retracted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<ProcedureKindReference>> FindReferencesAsync(
        string kindKey,
        CancellationToken cancellationToken = default);
}
