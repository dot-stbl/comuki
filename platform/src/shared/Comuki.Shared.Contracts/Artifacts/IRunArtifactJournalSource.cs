using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Artifacts;

/// <summary>
/// Reads from the orchestration engine that the packager needs to compose a bundle:
/// the terminal snapshot of a run (status, work-item result, journal stamp) and the
/// brief that was handed to the worker. Host binds the EF-backed implementation;
/// tests and design-time bind the null stubs.
/// </summary>
public interface IRunArtifactJournalSource
{
    /// <summary>Returns the terminal snapshot for a run, or null if the run is not yet terminal.</summary>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<RunTerminalSnapshot?> ReadTerminalAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the brief JSON the worker received, or null if unknown.</summary>
    /// <param name="workItemId">Work item that originated the run.</param>
    /// <param name="cancellationToken"></param>
    public Task<string?> ReadWorkItemBriefAsync(Guid workItemId, CancellationToken cancellationToken = default);
}
