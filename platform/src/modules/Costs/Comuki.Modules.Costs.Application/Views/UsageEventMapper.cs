using Comuki.Modules.Costs.Domain.Events;

namespace Comuki.Modules.Costs.Application.Views;

/// <summary>Entity → view mapping for usage events (hand-written, pure).</summary>
public static class UsageEventMapper
{
    /// <summary>Maps one usage event.</summary>
    /// <param name="usageEvent"></param>
    public static UsageEventView ToView(UsageEvent usageEvent)
    {
        return new UsageEventView(
            usageEvent.Id.Value,
            usageEvent.RunId,
            UsageSourceKeys.Of(usageEvent.Source),
            usageEvent.Model,
            usageEvent.InputTokens,
            usageEvent.OutputTokens,
            usageEvent.CostUsdMicros,
            usageEvent.OccurredAt);
    }
}
