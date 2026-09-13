using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Usage;

/// <summary>
/// Persistence port for usage events. Reads are exposed here so the
/// proxy pre-flight can sum proxy-source spend without taking a
/// dependency on the Costs module's internal Application assembly.
/// All wire types on the boundary are contracts-level records
/// (<see cref="UsageRecord"/>, <see cref="UsageEventSummary"/>, the
/// string <c>source</c> parameter on <see cref="SumProjectCostBySourceAsync"/>) — no entity / no Costs.Domain
/// reference.
/// </summary>
public interface IUsageEventStore
{
    /// <summary>Appends one usage event (built from the wire record inside the Costs module).</summary>
    /// <param name="record">The usage record to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the write.</param>
    public Task AddAsync(UsageRecord record, CancellationToken cancellationToken = default);

    /// <summary>Sums cost for a project (all time or since <paramref name="since"/>).</summary>
    /// <param name="projectId">Project whose spend is summed.</param>
    /// <param name="since">Optional lower bound (UTC) on the summed events.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the read.</param>
    public Task<long> SumProjectCostUsdMicrosAsync(
        ProjectId projectId,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums cost for a project, restricted to one usage source. The proxy
    /// pre-flight uses this to keep brain / worker spend out of its
    /// monthly cap calculation. <paramref name="source"/> is one of the
    /// <see cref="UsageSources"/> constants.
    /// </summary>
    /// <param name="projectId">Project whose spend is summed.</param>
    /// <param name="source">Usage source filter — a <see cref="UsageSources"/> constant.</param>
    /// <param name="since">Optional lower bound (UTC) on the summed events.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the read.</param>
    public Task<long> SumProjectCostBySourceAsync(
        ProjectId projectId,
        string source,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sums cost for a single run.</summary>
    /// <param name="runId">Run whose spend is summed.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the read.</param>
    public Task<long> SumRunCostUsdMicrosAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Lists recent events for a project, newest first.</summary>
    /// <param name="projectId">Project whose events are listed.</param>
    /// <param name="take">Maximum number of events to return.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the read.</param>
    public Task<IReadOnlyList<UsageEventSummary>> ListRecentAsync(
        ProjectId projectId,
        int take,
        CancellationToken cancellationToken = default);
}
