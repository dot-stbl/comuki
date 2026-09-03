using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Internal seam used by the host-composed worker-attachment endpoint and
/// by the bundle writer. Lists runs the packager should consider for
/// bundling — terminal-status rows that have not been packaged yet.
/// Lives in the application layer so the host (the only caller) reaches
/// the EF backing through the module boundary, not through direct context
/// access. Implementations must be <c>Scoped</c> (they use the artifacts
/// DbContext).
/// </summary>
public interface IRunArtifactRunSource
{
    /// <summary>
    /// Streams terminal runs whose artifacts have not been packaged yet,
    /// oldest terminal transition first. The packager iterates the
    /// sequence and bundles each in turn; the sequence is bounded so a
    /// single poll does not hog the worker.
    /// </summary>
    /// <param name="limit">Maximum runs to return this poll.</param>
    /// <param name="cancellationToken"></param>
    public IAsyncEnumerable<RunArtifactCandidate> ListUnbundledTerminalAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the project id of one run. The artifacts store scopes every
    /// object key by project, so the packager needs the project's id to
    /// build the storage prefix.
    /// </summary>
    /// <param name="runId">Run whose project id is requested.</param>
    /// <param name="cancellationToken"></param>
    public Task<ProjectId?> ReadProjectIdAsync(RunId runId, CancellationToken cancellationToken = default);
}

/// <summary>One run the packager should consider bundling.</summary>
/// <param name="RunId">Run whose terminal status has not yet been packaged.</param>
/// <param name="ProjectId">Owning project.</param>
public sealed record RunArtifactCandidate(RunId RunId, ProjectId ProjectId);
