using Comuki.Modules.Artifacts.Domain;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Bookkeeping port for the artifact packager — answers "has this run
/// already been packaged?" and records the outcome. Persisted in the
/// artifacts schema (one row per run); the EF implementation lives in the
/// Infrastructure project. The default registration is a no-op stub so the
/// application layer composes in isolation without a database.
/// </summary>
public interface IRunArtifactBundleStore
{
    /// <summary>True when the run has already been packaged (skip on next poll).</summary>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<bool> IsBundledAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Records the upload outcome — idempotent on a re-run.</summary>
    /// <param name="bundle">The bundle row to remember.</param>
    /// <param name="cancellationToken"></param>
    public Task RecordAsync(RunArtifactBundle bundle, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stub implementation: always reports <em>not bundled</em> and discards
/// <see cref="RecordAsync"/> calls. Used when the module composes without the
/// EF-backed store (unit tests, design-time). The host replaces this with
/// the EF-backed implementation via <c>TryAddSingleton</c>.
/// </summary>
public sealed class NullRunArtifactBundleStore : IRunArtifactBundleStore
{
    /// <inheritdoc />
    public Task<bool> IsBundledAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task RecordAsync(RunArtifactBundle bundle, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
