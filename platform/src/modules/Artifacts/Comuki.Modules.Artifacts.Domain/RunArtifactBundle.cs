namespace Comuki.Modules.Artifacts.Domain;

/// <summary>
/// Persistence row that records a run has been packaged — the packager
/// inspects this on every poll to skip runs it has already bundled. One
/// row per (project, run) — the unique constraint is enforced by the
/// <see cref="RunId"/> primary key.
/// </summary>
public sealed class RunArtifactBundle
{
    /// <summary>Run whose bundle has been uploaded.</summary>
    public Guid RunId { get; set; }

    /// <summary>Owning project — denormalised for the list endpoint.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Terminal status that triggered the upload.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Wall-clock when the packager finished the upload (UTC).</summary>
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>How many objects ended up in the bundle.</summary>
    public int ObjectCount { get; set; }
}
