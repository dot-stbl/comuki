using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Domain.DomainTypes;

/// <summary>
/// Per-project admission policy for one user-facing domain type
/// (<c>code</c>, <c>data</c>, <c>infra</c>, <c>research</c>, …) — the
/// "domain-user intake" gate of issue #11. Routing answers *which
/// profile* handles a domain type
/// (<see cref="Settings.ProjectDomainType"/>); admission answers the
/// prior question — *may this domain type run at all, and from where*.
/// <para>
/// Two independent switches, both stored as <c>text[]</c>:
/// <list type="bullet">
///     <item><see cref="AllowedSources"/> — allow-list of intake source
///     keys (<c>github</c>, <c>gitlab</c>, <c>jira</c>, <c>native</c>, …).
///     Empty means "any source"; non-empty admits only the listed keys.
///     Source keys are plain strings on purpose — the policy must not
///     depend on the Intake module (modules never reference siblings).</item>
///     <item><see cref="DeniedReasons"/> — stable reason codes that hold
///     this domain type back regardless of source (e.g.
///     <c>needs_human_review</c>). Empty means "nothing blocks it";
///     non-empty is a hard deny that reports exactly those codes.</item>
/// </list>
/// </para>
/// One row per (project, domain type) — enforced by a unique index.
/// </summary>
public sealed class DomainTypeAdmission
{
    internal DomainTypeAdmission()
    {
    }

    /// <summary>Upper bound of a domain-type key; mirrored by the column length.</summary>
    public const int MaxDomainTypeLength = 64;

    /// <summary>Reason code reported when the policy row itself is disabled.</summary>
    public const string DisabledReason = "admission_disabled";

    /// <summary>Reason code reported when the source is outside the allow-list.</summary>
    public const string SourceNotAllowedReason = "source_not_allowed";

    /// <summary>Strong-typed policy id (UUIDv7).</summary>
    public DomainTypeAdmissionId Id { get; private set; }

    /// <summary>Project the policy governs.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Normalized (trimmed, lower-cased) user-facing domain type key.</summary>
    public string DomainType { get; private set; } = string.Empty;

    /// <summary>Allow-list of intake source keys; empty means "any source".</summary>
    public string[] AllowedSources { get; private set; } = [];

    /// <summary>Stable reason codes blocking this domain type; empty means "not blocked".</summary>
    public string[] DeniedReasons { get; private set; } = [];

    /// <summary>Disabled policies deny everything for their domain type.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When the policy was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Normalizes a domain-type or source key the way the stored rows are
    /// normalized (trim + lower-case, invariant culture). Public so the
    /// application layer and the store look up rows with the very same key
    /// the entity persisted.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static string NormalizeKey(string key)
    {
        return key.Trim().ToLowerInvariant();
    }

    /// <summary>Creates an enabled policy; keys are normalized here.</summary>
    /// <param name="projectId"></param>
    /// <param name="domainType"></param>
    /// <param name="allowedSources">Allow-list of source keys; empty means "any source".</param>
    /// <param name="deniedReasons">Blocking reason codes; empty means "not blocked".</param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static DomainTypeAdmission Create(
        ProjectId projectId,
        string domainType,
        IReadOnlyList<string> allowedSources,
        IReadOnlyList<string> deniedReasons,
        DateTimeOffset now)
    {
        return new DomainTypeAdmission
        {
            Id = DomainTypeAdmissionId.New(),
            ProjectId = projectId,
            DomainType = NormalizeKey(domainType),
            AllowedSources = AdmissionKeys.NormalizeSet(allowedSources),
            DeniedReasons = AdmissionKeys.NormalizeSet(deniedReasons),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Partial update: a null argument leaves the stored value untouched
    /// (PATCH semantics, same as <see cref="Projects.Project.Update"/>).
    /// The domain type is the row's identity together with the project and
    /// is deliberately not editable.
    /// </summary>
    /// <param name="allowedSources"></param>
    /// <param name="deniedReasons"></param>
    /// <param name="enabled"></param>
    /// <param name="now"></param>
    public void Update(
        IReadOnlyList<string>? allowedSources,
        IReadOnlyList<string>? deniedReasons,
        bool? enabled,
        DateTimeOffset now)
    {
        if (allowedSources is { } nextSources)
        {
            AllowedSources = AdmissionKeys.NormalizeSet(nextSources);
        }

        if (deniedReasons is { } nextReasons)
        {
            DeniedReasons = AdmissionKeys.NormalizeSet(nextReasons);
        }

        if (enabled is { } nextEnabled)
        {
            Enabled = nextEnabled;
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// The truth table, as a pure function: which reason codes deny work of
    /// this domain type arriving from <paramref name="source"/>. An empty
    /// result means admitted. Order of checks — disabled row, explicit
    /// blocks, then the source allow-list — so the most specific
    /// human-authored reason wins over the generic one.
    /// </summary>
    /// <param name="source">Intake source key (<c>github</c>, <c>native</c>, …).</param>
    /// <returns>Reason codes; empty when the work is admitted.</returns>
    public IReadOnlyList<string> EvaluateDenials(string source)
    {
        return this switch
        {
            { Enabled: false } => [DisabledReason],

            { DeniedReasons.Length: > 0 } => DeniedReasons,

            // no allow-list at all — every source is welcome
            { AllowedSources.Length: 0 } => [],

            _ => AllowedSources.Contains(NormalizeKey(source), StringComparer.Ordinal)
                ? []
                : [SourceNotAllowedReason],
        };
    }
}

/// <summary>
/// Key normalization shared by <see cref="DomainTypeAdmission"/>'s factory
/// and updater. File-scoped so the entity keeps a single public surface
/// without a private helper method (<c>code-shape.md</c> §9).
/// </summary>
file static class AdmissionKeys
{
    /// <summary>
    /// Normalizes a key list: trim + lower-case each entry, drop blanks,
    /// de-duplicate, keep the caller's order. Materialized as an array so
    /// Npgsql maps it straight onto a <c>text[]</c> column.
    /// </summary>
    /// <param name="keys"></param>
    /// <returns></returns>
    public static string[] NormalizeSet(IReadOnlyList<string> keys)
    {
        return
        [
            .. keys
                .Select(DomainTypeAdmission.NormalizeKey)
                .Where(static key => key.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
    }
}
