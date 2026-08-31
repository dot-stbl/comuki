using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Journal;

/// <summary>
/// Port to the append-only run journal. Writers that mutate a run/work item
/// in the same store should append their transition event inside the same
/// transaction as the mutation; this port is the general-purpose surface for
/// standalone appends and timeline reads.
/// </summary>
public interface IRunJournal
{
    /// <summary>Appends one entry. Type and payload must be non-empty.</summary>
    /// <param name="entry"></param>
    /// <param name="cancellationToken"></param>
    public Task AppendAsync(RunEventEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of the run timeline, oldest first (ordered by
    /// <see cref="RunEventEntry.OccurredAt"/>, then id as tiebreak). Pages are
    /// 1-based.
    /// </summary>
    /// <param name="runId"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(
        RunId runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
