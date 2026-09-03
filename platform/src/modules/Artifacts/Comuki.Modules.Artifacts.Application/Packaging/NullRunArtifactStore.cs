using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Stub implementation of <see cref="IRunArtifactStore"/> that throws
/// on any real call — used when the application layer composes without
/// the MinIO-backed store (unit tests, design-time). The host replaces
/// this with the real MinIO store via
/// <see cref="AddArtifactsPersistence"/>.
/// </summary>
public sealed class NullRunArtifactStore : IRunArtifactStore
{
    /// <inheritdoc />
    public Task<Uri> UploadAsync(
        ProjectId projectId,
        RunId runId,
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "No IRunArtifactStore is registered; the host should bind a MinIO-backed store.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ArtifactPointer>> ListAsync(
        ProjectId projectId,
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "No IRunArtifactStore is registered; the host should bind a MinIO-backed store.");
    }
}
