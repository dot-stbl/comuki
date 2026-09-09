using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Usage;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Costs.Application.Recording;

/// <summary>
/// Default <see cref="IUsageRecorder"/>: persists the event, re-sums the
/// project, and on hard exceedance asks the host budget gate to stop the
/// attributed run. Soft exceedance is logged (attention surface later).
/// </summary>
/// <param name="store"></param>
/// <param name="budgets"></param>
/// <param name="gate"></param>
/// <param name="logger"></param>
public sealed class UsageRecorder(
    IUsageEventStore store,
    IProjectBudgetSettings budgets,
    IBudgetGate gate,
    ILogger<UsageRecorder> logger) : IUsageRecorder
{
    /// <inheritdoc />
    public async Task RecordAsync(UsageRecord record, CancellationToken cancellationToken = default)
    {
        // Reject unknown source keys at the contract boundary — the wire
        // format is the caller's promise; an empty / typo'd source would
        // silently bin into the database otherwise.
        UsageSourceKeys.Parse(record.Source);

        await store.AddAsync(record, cancellationToken);

        var caps = await budgets.GetAsync(record.ProjectId, cancellationToken);
        var spent = await store.SumProjectCostUsdMicrosAsync(record.ProjectId, cancellationToken: cancellationToken);

        if (caps.SoftLimitUsdMicros is { } soft && spent >= soft)
        {
            logger.LogWarning(
                "Project {ProjectId} soft budget exceeded: spent={SpentUsdMicros} soft={SoftLimitUsdMicros}",
                record.ProjectId,
                spent,
                soft);
        }

        if (caps.HardLimitUsdMicros is not { } hard || spent < hard)
        {
            return;
        }

        logger.LogError(
            "Project {ProjectId} hard budget exceeded: spent={SpentUsdMicros} hard={HardLimitUsdMicros}",
            record.ProjectId,
            spent,
            hard);

        if (record.RunId is not { } runId)
        {
            return;
        }

        await gate.HardStopAsync(runId, record.ProjectId, spent, hard, cancellationToken);
    }
}
