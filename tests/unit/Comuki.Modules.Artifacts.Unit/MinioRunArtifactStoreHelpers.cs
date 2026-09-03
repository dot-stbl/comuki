using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Unit;

/// <summary>
/// Thin re-export of <see cref="MinioRunArtifactStore.BuildObjectKey"/>
/// so unit tests can call the static helper without instantiating the
/// store. The helper is internal on the infrastructure side; the
/// InternalsVisibleTo wiring in the csproj exposes it.
/// </summary>
internal static class MinioRunArtifactStoreHelpers
{
    /// <summary>Test-friendly wrapper around the static key builder.</summary>
    public static string BuildObjectKeyPublic(ProjectId projectId, RunId runId, string relativePath)
    {
        return MinioRunArtifactStore.BuildObjectKey(projectId, runId, relativePath);
    }
}
