using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Costs;

/// <summary>
/// Cross-host port for metering: proxy / brain / worker reporters append
/// usage events without taking a dependency on the Costs module.
/// </summary>
public interface IUsageRecorder
{
    /// <summary>Appends one usage event and evaluates project budgets.</summary>
    /// <param name="record"></param>
    /// <param name="cancellationToken"></param>
    public Task RecordAsync(UsageRecord record, CancellationToken cancellationToken = default);
}

/// <summary>
/// One usage sample to record. Money is USD micros (1 USD = 1_000_000).
/// <paramref name="Source"/> is a stable wire key (<c>proxy</c>/<c>brain</c>/<c>worker</c>/<c>system</c>).
/// </summary>
/// <param name="ProjectId"></param>
/// <param name="RunId"></param>
/// <param name="Source"></param>
/// <param name="Model"></param>
/// <param name="InputTokens"></param>
/// <param name="OutputTokens"></param>
/// <param name="CostUsdMicros"></param>
/// <param name="OccurredAt"></param>
public sealed record UsageRecord(
    ProjectId ProjectId,
    RunId? RunId,
    string Source,
    string Model,
    int InputTokens,
    int OutputTokens,
    long CostUsdMicros,
    DateTimeOffset OccurredAt);
