using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Ports;

/// <summary>Persistence port for usage events.</summary>
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
