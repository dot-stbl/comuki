namespace Comuki.Host.Brain.Ports.ActiveRuns;

/// <summary>
/// Slice-A stub: no runs are visible through it. The agent tool reports
/// the empty list — honest output, wired shape, zero orchestration
/// coupling until the read API lands.
/// </summary>
public sealed class StubActiveRunCatalog : IActiveRunCatalog
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ActiveRunView>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ActiveRunView>>([]);
    }
}
