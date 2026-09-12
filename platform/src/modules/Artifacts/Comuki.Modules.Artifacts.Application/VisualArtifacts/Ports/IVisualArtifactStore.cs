using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;

/// <summary>
/// Port to the visual-artifact store. The application layer asks the
/// store to persist a blob + its metadata; the store implementation
/// writes bytes to MinIO and metadata to the artifacts schema. Reads
/// (download / find) return streams the host streams back to the caller.
/// </summary>
public interface IVisualArtifactStore
{
    /// <summary>
    /// Persists one artifact — uploads the bytes to MinIO and inserts
    /// the metadata row. When <see cref="VisualArtifactUploadRequest.ExplicitId"/>
    /// is supplied and an artifact with that id already exists, the store bumps
    /// its version and overwrites the bytes; otherwise it mints a new
    /// UUIDv7.
    /// </summary>
    /// <param name="request">Upload description (project, filename, mime, size, body, links).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The persisted artifact row.</returns>
    public Task<VisualArtifact> UploadVisualAsync(
        VisualArtifactUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the metadata row for one artifact, or <c>null</c> when the id is unknown.</summary>
    /// <param name="artifactId">Artifact id.</param>
    /// <param name="cancellationToken"></param>
    public Task<VisualArtifact?> FindAsync(
        VisualArtifactId artifactId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the metadata row for one artifact, scoped to the requesting
    /// project. A mismatch surfaces as <c>null</c> (the object axis — out
    /// of scope rows never reveal their existence to a forbidden subject).
    /// </summary>
    /// <param name="artifactId">Artifact id.</param>
    /// <param name="projectId">Owning project (the subject's scope).</param>
    /// <param name="cancellationToken"></param>
    public Task<VisualArtifact?> FindInProjectAsync(
        VisualArtifactId artifactId,
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the bytes of the latest version of one artifact that
    /// belongs to <paramref name="projectId"/>, plus its content-type.
    /// <c>null</c> when the artifact is unknown or lives in another
    /// project (the caller maps that to 404 — out-of-scope rows never
    /// reveal their existence to a forbidden subject).
    /// </summary>
    /// <param name="artifactId">Artifact id.</param>
    /// <param name="projectId">Owning project — the subject's scope.</param>
    /// <param name="cancellationToken"></param>
    public Task<VisualArtifactContent?> DownloadVisualInProjectAsync(
        VisualArtifactId artifactId,
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}
