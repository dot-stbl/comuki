using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Read model of per-project settings. <paramref name="Version"/> rides
/// along so API clients can echo it into the next PUT (optimistic
/// concurrency).
/// </summary>
/// <param name="ProjectId"></param>
/// <param name="MinIdle"></param>
/// <param name="MaxConcurrent"></param>
/// <param name="IdleTtlSeconds"></param>
/// <param name="ApproveRequired"></param>
/// <param name="KnowledgeEnabled"></param>
/// <param name="VerifyEnabled"></param>
/// <param name="ProxyEnabled"></param>
/// <param name="SoftBudgetUsdMicros"></param>
/// <param name="HardBudgetUsdMicros"></param>
/// <param name="UpdatedAt"></param>
/// <param name="Version"></param>
public sealed record ProjectSettingsView(
    ProjectId ProjectId,
    int MinIdle,
    int MaxConcurrent,
    int? IdleTtlSeconds,
    bool ApproveRequired,
    bool KnowledgeEnabled,
    bool VerifyEnabled,
    bool ProxyEnabled,
    long? SoftBudgetUsdMicros,
    long? HardBudgetUsdMicros,
    DateTimeOffset UpdatedAt,
    int Version);
