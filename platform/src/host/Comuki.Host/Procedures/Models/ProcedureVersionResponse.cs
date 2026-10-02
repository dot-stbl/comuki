using Comuki.Modules.Procedures.Application.Compiler.Model;

namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for a compiled procedure version
/// (<c>GET /api/v1/procedures/{projectId}/{procedureKey}</c> and
/// <c>GET /api/v1/procedures/versions/{versionId}</c>). Carries only the
/// identity + provenance fields the dashboard renders — the compiled
/// graph itself stays server-side.
/// </summary>
public sealed record ProcedureVersionResponse(
    string VersionId,
    Guid ProjectId,
    string ProcedureKey,
    string CatalogVersion,
    string SourceRef)
{
    /// <summary>Maps from the Application record to the wire shape.</summary>
    public static ProcedureVersionResponse From(CompiledProcedureVersion version)
    {
        return new ProcedureVersionResponse(
            version.VersionId,
            version.ProjectId,
            version.ProcedureKey,
            version.CatalogVersion,
            version.SourceRef);
    }
}
