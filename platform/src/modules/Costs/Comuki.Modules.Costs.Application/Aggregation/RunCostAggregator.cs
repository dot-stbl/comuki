using Comuki.Modules.Costs.Application.Ports;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Aggregation;

/// <summary>
/// Sums usage for a run or project. Pure over the store — no budget side
/// effects (those live in <c>UsageRecorder</c>).
/// </summary>
/// <param name="store"></param>
public sealed class RunCostAggregator(IUsageEventStore store)
{
    /// <summary>Total spend of a run in USD micros.</summary>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        return store.SumRunCostUsdMicrosAsync(runId, cancellationToken);
    }

    /// <summary>Total spend of a project in USD micros (optionally since <paramref name="since"/>).</summary>
    /// <param name="projectId"></param>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumProjectAsync(
        ProjectId projectId,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        return store.SumProjectCostUsdMicrosAsync(projectId, since, cancellationToken);
    }
}
