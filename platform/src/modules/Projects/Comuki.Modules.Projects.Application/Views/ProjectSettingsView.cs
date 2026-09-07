using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

///<summary>
/// Read model of per-project settings. <see cref="Version"/> rides along
/// so API clients can echo it into the next PUT (optimistic concurrency).
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
/// <param name="DomainType">Routing mode for user-facing domain types.</param>
/// <param name="CustomDomainTypesJson">Per-project JSON map of <c>domain-type → profile-key</c>.</param>
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
    ProjectDomainType DomainType,
    string? CustomDomainTypesJson,
    DateTimeOffset UpdatedAt,
    int Version);
