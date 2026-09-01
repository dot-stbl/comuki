namespace Comuki.Shared.Contracts.Runs;

/// <summary>
/// Read port to orchestration runs for surfaces that must not reach into the
/// engine (chat tools, future dashboards). Implemented in the host composition
/// root, which is the only place allowed to reference orchestration internals.
/// Read-only by contract.
/// </summary>
public interface IRunsReader
{
    /// <summary>Lists the most recent runs, newest first.</summary>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<RunSummary>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
