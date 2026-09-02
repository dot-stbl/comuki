using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Domain.Settings;

/// <summary>
/// Per-project settings row (one per project, created together with it):
/// scale quotas, the approval gate and the opt-in feature flags. Live
/// reload semantics: readers go through <c>IProjectSettingsStore</c>;
/// every mutation bumps <see cref="Version"/> (optimistic concurrency —
/// writers must present the version they read) so concurrent editors get
/// a conflict instead of a silent lost update.
/// </summary>
public sealed class ProjectSettings
{
    internal ProjectSettings()
    {
    }

    /// <summary>Default concurrency cap of a fresh project (mirrors the supervisor options default).</summary>
    public const int DefaultMaxConcurrent = 4;

    /// <summary>Project id — primary key, shared with the project row.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Warm-idle floor per profile: idle workers are never reaped below it.</summary>
    public int MinIdle { get; private set; }

    /// <summary>Cap on concurrently running workers per project.</summary>
    public int MaxConcurrent { get; private set; }

    /// <summary>Idle TTL in seconds; null means "use the engine default".</summary>
    public int? IdleTtlSeconds { get; private set; }

    /// <summary>When true, runs of this project wait for an explicit approval.</summary>
    public bool ApproveRequired { get; private set; }

    /// <summary>Opt-in Knowledge feature (MCP + retrieval).</summary>
    public bool KnowledgeEnabled { get; private set; }

    /// <summary>Opt-in Verify feature (generic-command gate).</summary>
    public bool VerifyEnabled { get; private set; }

    /// <summary>Opt-in Proxy feature (model gateway).</summary>
    public bool ProxyEnabled { get; private set; }

    /// <summary>
    /// Soft budget in USD micros (1 USD = 1_000_000); null = unlimited.
    /// Soft exceedance is advisory (attention), not a stop.
    /// </summary>
    public long? SoftBudgetUsdMicros { get; private set; }

    /// <summary>
    /// Hard budget in USD micros; null = unlimited. Hard exceedance cancels
    /// the attributed run via the host budget gate.
    /// </summary>
    public long? HardBudgetUsdMicros { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency version; starts at 1, +1 per mutation.</summary>
    public int Version { get; private set; }

    /// <summary>Default settings row created with every new project.</summary>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    public static ProjectSettings CreateDefaults(ProjectId projectId, DateTimeOffset now)
    {
        return new ProjectSettings
        {
            ProjectId = projectId,
            MinIdle = 0,
            MaxConcurrent = DefaultMaxConcurrent,
            IdleTtlSeconds = null,
            ApproveRequired = false,
            KnowledgeEnabled = false,
            VerifyEnabled = false,
            ProxyEnabled = false,
            SoftBudgetUsdMicros = null,
            HardBudgetUsdMicros = null,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Replaces the tunables and bumps <see cref="Version"/>. The caller is
    /// expected to have verified the presented version against the loaded
    /// row; the store re-checks (and the version concurrency token guards)
    /// before anything is written.
    /// </summary>
    /// <param name="minIdle"></param>
    /// <param name="maxConcurrent"></param>
    /// <param name="idleTtlSeconds"></param>
    /// <param name="approveRequired"></param>
    /// <param name="knowledgeEnabled"></param>
    /// <param name="verifyEnabled"></param>
    /// <param name="proxyEnabled"></param>
    /// <param name="softBudgetUsdMicros"></param>
    /// <param name="hardBudgetUsdMicros"></param>
    /// <param name="now"></param>
    public void Apply(
        int minIdle,
        int maxConcurrent,
        int? idleTtlSeconds,
        bool approveRequired,
        bool knowledgeEnabled,
        bool verifyEnabled,
        bool proxyEnabled,
        long? softBudgetUsdMicros,
        long? hardBudgetUsdMicros,
        DateTimeOffset now)
    {
        MinIdle = minIdle;
        MaxConcurrent = maxConcurrent;
        IdleTtlSeconds = idleTtlSeconds;
        ApproveRequired = approveRequired;
        KnowledgeEnabled = knowledgeEnabled;
        VerifyEnabled = verifyEnabled;
        ProxyEnabled = proxyEnabled;
        SoftBudgetUsdMicros = softBudgetUsdMicros;
        HardBudgetUsdMicros = hardBudgetUsdMicros;
        UpdatedAt = now;
        Version++;
    }
}
