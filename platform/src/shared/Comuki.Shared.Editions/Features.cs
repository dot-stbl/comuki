using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions;

/// <summary>
/// The single source of truth for every paid capability key (issue #164
/// E3, E11). Community is the default and is out of every row below —
/// every entry here requires at least <see cref="EditionTiers.Team"/>
/// today. A third tier (OQ3) is added by appending rows with a higher
/// <see cref="Feature.MinimumRank"/>, never by editing this file's shape.
/// </summary>
public static class Features
{
    /// <summary>Enterprise identity: SSO / OIDC providers, SCIM provisioning, configurable roles (#95), audit export.</summary>
    public static readonly Feature EnterpriseSso = Feature.Define(
        "enterprise-sso",
        "Enterprise identity: SSO / OIDC providers, SCIM provisioning, configurable roles (#95), audit export.",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Kubernetes compute, worker pools / isolation classes (#100), autoscaling, HA / backplane (#101).</summary>
    public static readonly Feature ScaleAndIsolation = Feature.Define(
        "scale-isolation",
        "Kubernetes compute, worker pools / isolation classes (#100), autoscaling, HA / backplane (#101).",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Infrastructure memory beyond the Community baseline.</summary>
    public static readonly Feature InfraMemory = Feature.Define(
        "infra-memory",
        "Infrastructure memory: cross-run operational knowledge beyond the Community baseline.",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Background LLM watchers monitoring runs outside the active chat session.</summary>
    public static readonly Feature BackgroundLlmWatchers = Feature.Define(
        "background-llm-watchers",
        "Background LLM watchers monitoring runs outside the active chat session.",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>AgentEval dashboard: automated agent-quality evaluation reporting.</summary>
    public static readonly Feature AgentEval = Feature.Define(
        "agenteval",
        "AgentEval dashboard: automated agent-quality evaluation reporting.",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>White-label branding across the dashboard and generated reports.</summary>
    public static readonly Feature WhiteLabel = Feature.Define(
        "white-label",
        "White-label branding across the dashboard and generated reports.",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Attach more than one repository / create more than one project per workspace (#163).</summary>
    public static readonly Feature MultiRepo = Feature.Define(
        "multi-repo",
        "Attach more than one repository / create more than one project per workspace (#163).",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>
    /// Worker commit attribution trailer control (#165 — this row is the
    /// edition-gate mechanism only; #165's own change owns what actually
    /// gets gated).
    /// </summary>
    public static readonly Feature WorkerCommitAttribution = Feature.Define(
        "worker-commit-attribution",
        "Control over the 'Generated-by: Comuki vX.Y.Z' worker-commit trailer (#165).",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Every declared feature, sorted by key. Throws at type-init if two entries share a key.</summary>
    public static readonly IReadOnlyList<Feature> All = EditionCatalogGuard.EnsureUniqueSortedByKey(
        [EnterpriseSso, ScaleAndIsolation, InfraMemory, BackgroundLlmWatchers, AgentEval, WhiteLabel, MultiRepo, WorkerCommitAttribution],
        static feature => feature.Key.Value,
        catalogName: "Features");
}
