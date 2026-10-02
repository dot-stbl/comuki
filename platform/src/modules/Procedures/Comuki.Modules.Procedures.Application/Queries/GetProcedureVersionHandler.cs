using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.ProcedureVersions;

namespace Comuki.Modules.Procedures.Application.Queries;

/// <summary>
/// Read handler: returns a single compiled version by its content-addressed id.
/// </summary>
/// <param name="versionStore">The immutable version store.</param>
public sealed class GetProcedureVersionHandler(IProcedureVersionStore versionStore)
{
    /// <summary>Returns the version or null when absent.</summary>
    /// <param name="versionId">The content-addressed version id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CompiledProcedureVersion?> HandleAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        return await versionStore.GetAsync(versionId, cancellationToken);
    }
}
