using System.Text.Json.Serialization;
using Comuki.Modules.Projects.Domain.Settings;

namespace Comuki.Host.Projects.Models;

/// <summary>
/// Wire body of PUT /api/v1/projects/{projectId}/settings.
/// <see cref="Version"/> is the version the client read — a stale version
/// is refused with 409.
/// </summary>
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
/// <param name="DomainType">Routing mode for user-facing domain types. Serialised as a string for readability.</param>
/// <param name="CustomDomainTypesJson">Per-project JSON map of <c>domain-type → profile-key</c>.</param>
public sealed record UpdateSettingsRequest(
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
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProjectDomainType DomainType,
    string? CustomDomainTypesJson);
