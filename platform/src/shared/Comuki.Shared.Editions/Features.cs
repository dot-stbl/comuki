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

    /// <summary>
    /// Live-session steering of in-flight runs (add-orchestra §1 — Baton).
    /// The first production caller of <c>WorkerCommandHub</c>: the
    /// <c>POST /api/v1/runs/{runId}/steer</c> endpoint requires this key,
    /// and the registered gate lives on <c>RunsController.SteerAsync</c>
    /// (the handler that resolves <c>runId → WorkItem → LeasedBy → WorkerId</c>
    /// and either writes a follow-up WorkItem on the no-LiveSession
    /// runtime or hands the typed turn to <c>WorkerCommandHub</c>).
    /// </summary>
    public static readonly Feature Steering = Feature.Define(
        "steering",
        "Live-session steering of in-flight runs (add-orchestra §1 — Baton).",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>
    /// Run verification axis (add-orchestra §3 — Coda): the gate-provider
    /// registry, the <c>gate.evaluated</c> journal event, the
    /// <c>VerificationRecord</c> per work item, and the derived
    /// <c>GET /api/v1/runs/{runId}/verification</c> view. The first
    /// paid-feature call-site for the key is the host's
    /// gate-provider registration handler
    /// (<c>VerificationHostExtensions.AddOrchestrationVerification</c>):
    /// Community answers a 403 on the registration call (the arch test
    /// catches an orphan key), and the same gate covers the verification
    /// view (the view's only permission is <c>run:read</c>, but the
    /// gate row's existence is what makes the axis observable).
    /// </summary>
    public static readonly Feature Verification = Feature.Define(
        "verification",
        "Run verification axis: gate providers, evidence, derived view (add-orchestra §3 — Coda).",
        minimumRank: EditionTiers.Team.Rank);

    /// <summary>Every declared feature, sorted by key. Throws at type-init if two entries share a key.</summary>
    public static readonly IReadOnlyList<Feature> All = EditionCatalogGuard.EnsureUniqueSortedByKey(
        [EnterpriseSso, ScaleAndIsolation, InfraMemory, BackgroundLlmWatchers, AgentEval, WhiteLabel, MultiRepo, WorkerCommitAttribution, Steering, Verification],
        static feature => feature.Key.Value,
        catalogName: "Features");
}
