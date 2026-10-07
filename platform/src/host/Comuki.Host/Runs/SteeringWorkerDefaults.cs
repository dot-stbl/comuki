namespace Comuki.Host.Runs;

/// <summary>
/// Worker launch defaults for follow-up WorkItems the steer endpoint
/// stages (add-orchestra §1 — Baton, Phase 1a no-LiveSession path).
/// Mirrors <c>ChatWorkerDefaults</c> / <c>IntegrationWorkerDefaults</c>:
/// the worker image and the pinned profiles-ref the follow-up claim
/// labels carry, and the default <c>profileKey</c> when the originating
/// run has none the resolver can reach. Profile overrides per
/// connection live in the project's settings jsonb and win over this
/// default.
/// </summary>
public sealed class SteeringWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Steering:Worker";

    /// <summary>
    /// Profile key the follow-up WorkItem is created with when the
    /// originating live work item's profile is unavailable (defensive
    /// only — the resolver always picks up the live item's profile
    /// first). Default <c>implement</c> matches the chat/integrations
    /// baselines; a deployment that wants every steer to hit a
    /// "fix-up" profile overrides the section in
    /// <c>appsettings.json</c>.
    /// </summary>
    public string ProfileKey { get; init; } = "implement";

    /// <summary>Worker image (with digest) steer follow-ups claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo steer follow-ups claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
