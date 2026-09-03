using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Stub <see cref="IRunArtifactJournalSource"/> that always reports
/// "no terminal snapshot" — used when the application layer composes
/// without the EF-backed source (unit tests, design-time). The host
/// replaces this with the EF implementation via
/// <see cref="AddArtifactsPersistence"/>.
/// </summary>
public sealed class NullRunArtifactJournalSource : IRunArtifactJournalSource
{
    /// <inheritdoc />
    public Task<RunTerminalSnapshot?> ReadTerminalAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<RunTerminalSnapshot?>(null);
    }

    /// <inheritdoc />
    public Task<string?> ReadWorkItemBriefAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
