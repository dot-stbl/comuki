using Comuki.Modules.Procedures.Application.Compiler.Model;

namespace Comuki.Modules.Procedures.Application.ProcedureVersions;

/// <summary>
/// Persistent store for compiled procedure versions: an immutable
/// content-addressed set of compiled plans plus their resolution
/// snapshot. Compiled versions never mutate after publication — a
/// retry creates a new attempt that pins the then-current version
/// (spec requirement "Retry re-pins"). Implementations land in the
/// Infrastructure layer (task 2.4); this port is the seam.
/// </summary>
/// <remarks>
/// The retraction and bump semantics the store enforces live in the
/// Domain (task 1.2 catalog validator) and the compile gate (task 2.3)
/// — the store itself is a content-addressed write-once read-many
/// surface keyed by the content-addressed version id.
/// </remarks>
public interface IProcedureVersionStore
{
    /// <summary>
    /// Stores a newly compiled version. The first write with a given
    /// <see cref="CompiledProcedureVersion.VersionId"/> wins; subsequent
    /// writes with the same id are byte-identical no-ops (the store
    /// deduplicates by content).
    /// </summary>
    /// <param name="version">The compiled version to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SaveAsync(
        CompiledProcedureVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the compiled version with the given id, or null when no
    /// such version exists. Reads are byte-identical to the stored
    /// record (spec requirement "pinned version is byte-identical on
    /// re-read").
    /// </summary>
    /// <param name="versionId">The content-addressed version id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CompiledProcedureVersion?> GetAsync(
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every version published for the given
    /// (<paramref name="projectId"/>, <paramref name="procedureKey"/>).
    /// Used by <see cref="Domain.Validation.Ports.IProcedureKindUsageLookup"/>
    /// when the catalog validator checks whether a kind is still
    /// referenced (task 1.2 scenario: "Retraction blocked by reference").
    /// </summary>
    /// <param name="projectId">Project that owns the procedure.</param>
    /// <param name="procedureKey">Stable procedure key inside the project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<CompiledProcedureVersion>> ListByProcedureAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default);
}
