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

    /// <summary>
    /// Fixed profile key every Standard / Hybrid project falls back to
    /// when the domain type has no explicit mapping. Mirrors the default
    /// profile in the control plane (<c>profiles/implement.md</c>).
    /// </summary>
    public const string DefaultDomainProfileKey = "implement";

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

    /// <summary>
    /// Routing mode for user-facing domain types (issue #11 slice 1).
    /// Standard = default profile only; Custom = JSON map only;
    /// Hybrid = default then JSON map.
    /// </summary>
    public ProjectDomainType DomainType { get; private set; }

    /// <summary>
    /// Per-project JSON map of <c>domain-type → profile-key</c>.
    /// Read when <see cref="DomainType"/> is <see cref="ProjectDomainType.Custom"/>
    /// or <see cref="ProjectDomainType.Hybrid"/>. Ignored for
    /// <see cref="ProjectDomainType.Standard"/>. Null is allowed; a
    /// Custom project with null JSON has no resolvable domain.
    /// </summary>
    public string? CustomDomainTypesJson { get; private set; }

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
            DomainType = ProjectDomainType.Standard,
            CustomDomainTypesJson = null,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Reconstructs a snapshot from already-persisted state, every field
    /// supplied explicitly including <see cref="Version"/> — unlike
    /// <see cref="Apply"/>, which always advances the version by one, this
    /// is not a mutation. The distributed settings cache
    /// (<c>DistributedProjectSettingsCache</c>) is the caller: it stores a
    /// JSON snapshot of a settings row in Redis and needs to rebuild the
    /// exact same row (same version) on the next read, without touching
    /// EF Core's own materialization path (the internal parameterless
    /// constructor stays EF's alone).
    /// </summary>
    /// <param name="projectId">Primary key — the project this snapshot belongs to.</param>
    /// <param name="minIdle">See <see cref="MinIdle"/>.</param>
    /// <param name="maxConcurrent">See <see cref="MaxConcurrent"/>.</param>
    /// <param name="idleTtlSeconds">See <see cref="IdleTtlSeconds"/>.</param>
    /// <param name="approveRequired">See <see cref="ApproveRequired"/>.</param>
    /// <param name="knowledgeEnabled">See <see cref="KnowledgeEnabled"/>.</param>
    /// <param name="verifyEnabled">See <see cref="VerifyEnabled"/>.</param>
    /// <param name="proxyEnabled">See <see cref="ProxyEnabled"/>.</param>
    /// <param name="softBudgetUsdMicros">See <see cref="SoftBudgetUsdMicros"/>.</param>
    /// <param name="hardBudgetUsdMicros">See <see cref="HardBudgetUsdMicros"/>.</param>
    /// <param name="domainType">See <see cref="DomainType"/>.</param>
    /// <param name="customDomainTypesJson">See <see cref="CustomDomainTypesJson"/>.</param>
    /// <param name="updatedAt">Captured mutation timestamp — carried through as-is, not re-stamped.</param>
    /// <param name="version">Exact version to restore — unlike <see cref="Apply"/>, this is not incremented.</param>
    public static ProjectSettings FromSnapshot(
        ProjectId projectId,
        int minIdle,
        int maxConcurrent,
        int? idleTtlSeconds,
        bool approveRequired,
        bool knowledgeEnabled,
        bool verifyEnabled,
        bool proxyEnabled,
        long? softBudgetUsdMicros,
        long? hardBudgetUsdMicros,
        ProjectDomainType domainType,
        string? customDomainTypesJson,
        DateTimeOffset updatedAt,
        int version)
    {
        return new ProjectSettings
        {
            ProjectId = projectId,
            MinIdle = minIdle,
            MaxConcurrent = maxConcurrent,
            IdleTtlSeconds = idleTtlSeconds,
            ApproveRequired = approveRequired,
            KnowledgeEnabled = knowledgeEnabled,
            VerifyEnabled = verifyEnabled,
            ProxyEnabled = proxyEnabled,
            SoftBudgetUsdMicros = softBudgetUsdMicros,
            HardBudgetUsdMicros = hardBudgetUsdMicros,
            DomainType = domainType,
            CustomDomainTypesJson = customDomainTypesJson,
            UpdatedAt = updatedAt,
            Version = version,
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
    /// <param name="domainType"></param>
    /// <param name="customDomainTypesJson"></param>
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
        ProjectDomainType domainType,
        string? customDomainTypesJson,
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
        DomainType = domainType;
        CustomDomainTypesJson = customDomainTypesJson;
        UpdatedAt = now;
        Version++;
    }
}
