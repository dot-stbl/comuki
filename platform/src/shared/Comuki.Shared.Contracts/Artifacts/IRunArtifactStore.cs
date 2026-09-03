using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Artifacts;

/// <summary>
/// Run-bundle storage. One object per <c>(projectId, runId, relativePath)</c>
/// in a host-configured bucket; the host binds a MinIO-backed implementation.
/// </summary>
public interface IRunArtifactStore
{
    /// <summary>Uploads one object and returns its canonical URI in the bucket.</summary>
    /// <param name="projectId">Owning project — scopes the object key prefix.</param>
    /// <param name="runId">Run — second scope of the object key prefix.</param>
    /// <param name="relativePath">Object name under the <c>{projectId}/{runId}/</c> prefix (e.g. <c>brief.json</c>).</param>
    /// <param name="content">Object body.</param>
    /// <param name="contentType">MIME type written to the object metadata.</param>
    /// <param name="cancellationToken"></param>
    public Task<Uri> UploadAsync(
        ProjectId projectId,
        RunId runId,
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every object already in the bundle for one run.</summary>
    /// <param name="projectId"></param>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ArtifactPointer>> ListAsync(
        ProjectId projectId,
        RunId runId,
        CancellationToken cancellationToken = default);
}
