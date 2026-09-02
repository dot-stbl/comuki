namespace Comuki.Host.Brain.Ports.ActiveRuns;

/// <summary>
/// The brain's view of active runs (the <c>list_active_runs</c> tool).
/// Slice A ships a local stub — the real catalog arrives with the
/// orchestration read API; the port shape is what the agent loop codes
/// against and what unit tests fake.
/// </summary>
public interface IActiveRunCatalog
{
    /// <summary>Every active run, newest first.</summary>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ActiveRunView>> ListAsync(CancellationToken cancellationToken = default);
}
