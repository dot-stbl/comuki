using Comuki.Shared.Contracts.Usage;

namespace Comuki.Modules.Costs.Application.Views;

/// <summary>DTO → view mapping for usage events (hand-written, pure).</summary>
public static class UsageEventMapper
{
    /// <summary>Maps one contract-level summary to the API view shape.</summary>
    /// <param name="summary">Contract summary produced by <see cref="IUsageEventStore.ListRecentAsync"/>.</param>
    public static UsageEventView ToView(UsageEventSummary summary)
    {
        return new UsageEventView(
            summary.Id,
            summary.RunId,
            summary.Source,
            summary.Model,
            summary.InputTokens,
            summary.OutputTokens,
            summary.CostUsdMicros,
            summary.OccurredAt);
    }
}
