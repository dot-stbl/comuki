namespace Comuki.Modules.Procedures.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for a compiled procedure version. Immutable after insert —
/// the content-addressed <c>version_id</c> is the PK; a re-insert with
/// the same id is a no-op (content deduplication).
/// </summary>
public sealed class CompiledProcedureVersionEntity
{
    /// <summary>Content-addressed SHA-256 id (PK).</summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>The project the procedure belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Stable key the project uses to reference the procedure.</summary>
    public string ProcedureKey { get; set; } = string.Empty;

    /// <summary>The node-kind catalog version the compile resolved against.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Provenance — the git ref the definition was loaded from.</summary>
    public string SourceRef { get; set; } = string.Empty;

    /// <summary>Canonical JSON serialization of the validated graph.</summary>
    public string GraphJson { get; set; } = string.Empty;

    /// <summary>When the version was compiled and stored (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
