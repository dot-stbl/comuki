using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Usage;

/// <summary>
/// Persistence port for usage events. Reads are exposed here so the
/// proxy pre-flight can sum proxy-source spend without taking a
/// dependency on the Costs module's internal Application assembly.
/// </summary>
public interface IUsageEventStore
{
    /// <summary>Appends one event.</summary>
    /// <param name="usageEvent"></param>
    /// <param name="cancellationToken"></param>
    public Task AddAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default);

    /// <summary>Sums cost for a project (all time or since <paramref name="since"/>).</summary>
    /// <param name="projectId"></param>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumProjectCostUsdMicrosAsync(
        ProjectId projectId,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums cost for a project, restricted to one usage source. The proxy
    /// pre-flight uses this to keep brain / worker spend out of its
    /// monthly cap calculation.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="source"></param>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumProjectCostBySourceAsync(
        ProjectId projectId,
        UsageSource source,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sums cost for a single run.</summary>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumRunCostUsdMicrosAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Lists recent events for a project, newest first.</summary>
    /// <param name="projectId"></param>
    /// <param name="take"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<UsageEvent>> ListRecentAsync(
        ProjectId projectId,
        int take,
        CancellationToken cancellationToken = default);
}
