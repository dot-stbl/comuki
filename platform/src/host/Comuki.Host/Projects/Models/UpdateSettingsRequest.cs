namespace Comuki.Host.Projects.Models;

/// <summary>
/// Wire body of PUT /api/v1/projects/{projectId}/settings.
/// <paramref name="Version"/> is the version the client read — a stale
/// version is refused with 409.
/// </summary>
/// <param name="Version"></param>
/// <param name="MinIdle"></param>
/// <param name="MaxConcurrent"></param>
/// <param name="IdleTtlSeconds"></param>
/// <param name="ApproveRequired"></param>
/// <param name="KnowledgeEnabled"></param>
/// <param name="VerifyEnabled"></param>
/// <param name="ProxyEnabled"></param>
public sealed record UpdateSettingsRequest(
    int Version,
    int MinIdle,
    int MaxConcurrent,
    int? IdleTtlSeconds,
    bool ApproveRequired,
    bool KnowledgeEnabled,
    bool VerifyEnabled,
    bool ProxyEnabled);
