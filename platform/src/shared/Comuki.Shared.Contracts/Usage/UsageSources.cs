using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Usage;

/// <summary>
/// Stable wire keys for <see cref="UsageRecord.Source"/>. The corresponding
/// Costs-side enum (<c>UsageSource</c>) and mapping helpers live in
/// <c>Comuki.Modules.Costs.Domain.Events</c>; these strings are the only
/// representation that crosses module boundaries.
/// </summary>
public static class UsageSources
{
    /// <summary>Model gateway (YARP proxy).</summary>
    public const string Proxy = "proxy";

    /// <summary>Brain host (leading model).</summary>
    public const string Brain = "brain";

    /// <summary>Worker / translator report.</summary>
    public const string Worker = "worker";

    /// <summary>Manual / test / system injection.</summary>
    public const string System = "system";
}

/// <summary>
/// Read-side projection of a usage event (no entity reach across
/// Shared.Contracts → Costs.Domain). Money fields are USD micros
/// (1 USD = 1_000_000). <see cref="Source"/> is one of the
/// <see cref="UsageSources"/> constants.
/// </summary>
/// <param name="Id">Event id.</param>
/// <param name="RunId">Optional run attribution.</param>
/// <param name="Source">Wire key (<c>proxy</c>/<c>brain</c>/<c>worker</c>/<c>system</c>).</param>
/// <param name="Model">Provider-native model id.</param>
/// <param name="InputTokens">Prompt tokens.</param>
/// <param name="OutputTokens">Completion tokens.</param>
/// <param name="CostUsdMicros">Money field.</param>
/// <param name="OccurredAt">When the usage happened.</param>
public sealed record UsageEventSummary(
    Guid Id,
    RunId? RunId,
    string Source,
    string Model,
    int InputTokens,
    int OutputTokens,
    long CostUsdMicros,
    DateTimeOffset OccurredAt);
