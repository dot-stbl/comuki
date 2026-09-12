namespace Comuki.Modules.Artifacts.Domain.VisualArtifacts;

/// <summary>
/// Persistence row for one published visual artifact — the metadata
/// side of an object store entry. Bytes live in the configured
/// MinIO bucket under <c>visual_artifacts/{projectId}/{artifactId}/{version}</c>;
/// this row is what the content proxy + list endpoints read to resolve
/// the storage pointer and to apply the scope query filter (a
/// subject who cannot see the project never sees the row).
/// </summary>
public sealed class VisualArtifact
{
    /// <summary>Artifact id (UUIDv7; time-ordered so list pagination is cheap).</summary>
    public Guid Id { get; set; }

    /// <summary>Owning project — denormalised for the scope filter.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Object key component — monotonic per <see cref="Id"/>, 1 on the first publish.</summary>
    public int Version { get; set; }

    /// <summary>Original file name from the publisher.</summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>MIME type written to the object and served on content GET.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Object size in bytes, recorded at upload time.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Optional human title (slice 4 — brain mockup label).</summary>
    public string? Title { get; set; }

    /// <summary>UTC wall-clock when the publish landed.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Who published — see <see cref="VisualArtifactCreatedByWire"/>.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Optional run id the artifact is linked to (always set for worker publishes).</summary>
    public Guid? RunId { get; set; }

    /// <summary>Optional work item id the artifact is linked to.</summary>
    public Guid? WorkItemId { get; set; }

    /// <summary>Optional chat session id (slice 4 — brain mockup in chat).</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Optional ticket id the artifact is linked to.</summary>
    public Guid? TicketId { get; set; }
}
