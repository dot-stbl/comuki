using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.ProcedureVersions;

namespace Comuki.Modules.Procedures.Application.Queries;

/// <summary>
/// Read handler: lists every published version for a procedure, newest
/// first. The duty board and the retraction guard both read this.
/// </summary>
/// <param name="versionStore">The immutable version store.</param>
public sealed class ListProcedureVersionsHandler(IProcedureVersionStore versionStore)
{
    /// <summary>Returns all versions for the procedure, newest first.</summary>
    /// <param name="projectId">The project that owns the procedure.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<CompiledProcedureVersion>> HandleAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default)
    {
        return await versionStore.ListByProcedureAsync(projectId, procedureKey, cancellationToken);
    }
}
