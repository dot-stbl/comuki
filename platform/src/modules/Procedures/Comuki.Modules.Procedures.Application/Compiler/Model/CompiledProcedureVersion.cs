namespace Comuki.Modules.Procedures.Application.Compiler.Model;

/// <summary>
/// The compiled, immutable, content-addressed result of compiling a
/// procedure definition. Carries the content-addressed id the runtime
/// pins against, the resolution snapshot, and the canonical graph JSON
/// the runtime materializes from.
/// </summary>
/// <param name="VersionId">Content-addressed id of the compiled version.</param>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">Stable key the project uses to reference the procedure.</param>
/// <param name="CatalogVersion">The procedure-node-kind catalog version the compile resolved against.</param>
/// <param name="SourceRef">Provenance — the git ref the definition was loaded from.</param>
/// <param name="GraphJson">Canonical JSON serialization of the validated graph (the compile output).</param>
public sealed record CompiledProcedureVersion(
    string VersionId,
    Guid ProjectId,
    string ProcedureKey,
    string CatalogVersion,
    string SourceRef,
    string GraphJson);
