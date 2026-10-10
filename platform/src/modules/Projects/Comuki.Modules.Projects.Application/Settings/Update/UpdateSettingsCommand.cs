using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Settings.Update;

/// <summary>
/// Replaces the settings of a project. <see cref="Version"/> is the
/// version the client read (optimistic concurrency): a writer presenting a
/// stale version gets <see cref="ProjectSettingsConflictException"/> and
/// must re-read.
/// </summary>
/// <param name="ProjectId"></param>
/// <param name="Version"></param>
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
/// <param name="CustomDomainTypesJson">
/// Per-project JSON map of <c>domain-type → profile-key</c>. Null for
/// Standard projects; required (non-null, non-empty) for Custom projects;
/// optional for Hybrid projects.
/// </param>
public sealed record UpdateSettingsCommand(
    ProjectId ProjectId,
    int Version,
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
    string? CustomDomainTypesJson);
