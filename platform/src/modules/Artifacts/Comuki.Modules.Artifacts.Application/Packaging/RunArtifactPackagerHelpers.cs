using System.Text;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Packager-side helpers for the artifact upload pipeline. Extracted from
/// <see cref="RunArtifactPackager"/> so the packager class holds only
/// orchestration (per <c>class-layout-and-tooling.md §1a</c>).
/// </summary>
internal static class RunArtifactPackagerHelpers
{
    /// <summary>Uploads a string body as a fresh <see cref="MemoryStream"/>; UTF-8, no BOM.</summary>
    /// <param name="store">Artifact store to upload into.</param>
    /// <param name="projectId">Project id of the run.</param>
    /// <param name="runId">Run id scoping the object key.</param>
    /// <param name="relativePath">Path inside the run's bundle prefix.</param>
    /// <param name="content">UTF-8 string body to upload.</param>
    /// <param name="contentType">Content-Type for the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task UploadTextAsync(
        IRunArtifactStore store,
        ProjectId projectId,
        RunId runId,
        string relativePath,
        string content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(bytes, writable: false);
        await store.UploadAsync(projectId, runId, relativePath, stream, contentType, cancellationToken);
    }
}
