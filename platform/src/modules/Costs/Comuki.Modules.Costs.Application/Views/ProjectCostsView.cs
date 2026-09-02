using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Views;

/// <summary>
/// Project cost summary for GET /api/v1/projects/{id}/costs.
/// Money fields are USD micros (1 USD = 1_000_000).
/// </summary>
/// <param name="ProjectId"></param>
/// <param name="SpentUsdMicros"></param>
/// <param name="SoftLimitUsdMicros"></param>
/// <param name="HardLimitUsdMicros"></param>
/// <param name="SoftExceeded"></param>
/// <param name="HardExceeded"></param>
/// <param name="Recent"></param>
public sealed record ProjectCostsView(
    ProjectId ProjectId,
    long SpentUsdMicros,
    long? SoftLimitUsdMicros,
    long? HardLimitUsdMicros,
    bool SoftExceeded,
    bool HardExceeded,
    IReadOnlyList<UsageEventView> Recent);

/// <summary>One usage row in the costs feed.</summary>
/// <param name="Id"></param>
/// <param name="RunId"></param>
/// <param name="Source"></param>
/// <param name="Model"></param>
/// <param name="InputTokens"></param>
/// <param name="OutputTokens"></param>
/// <param name="CostUsdMicros"></param>
/// <param name="OccurredAt"></param>
public sealed record UsageEventView(
    Guid Id,
    RunId? RunId,
    string Source,
    string Model,
    int InputTokens,
    int OutputTokens,
    long CostUsdMicros,
    DateTimeOffset OccurredAt);
