using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Stub <see cref="IRunArtifactRunSource"/> that yields nothing — used
/// when the application layer composes without the EF-backed source
/// (unit tests, design-time). The host replaces this with the EF
/// implementation via <see cref="AddArtifactsPersistence"/>.
/// </summary>
public sealed class NullRunArtifactRunSource : IRunArtifactRunSource
{
    /// <inheritdoc />
    public IAsyncEnumerable<RunArtifactCandidate> ListUnbundledTerminalAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return EmptyAsync();
    }

    /// <inheritdoc />
    public Task<ProjectId?> ReadProjectIdAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ProjectId?>(null);
    }

    private static async IAsyncEnumerable<RunArtifactCandidate> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
