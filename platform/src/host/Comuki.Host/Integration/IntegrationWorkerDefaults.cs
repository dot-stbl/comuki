namespace Comuki.Host.Integration;

/// <summary>
/// Worker launch defaults for integrations-created inbound items: the
/// worker image and the pinned profiles-ref. The worker
/// <c>profileKey</c> is no longer hardcoded —
/// <see cref="Modules.Integrations.Application.Ports.Admission.IIntegrationProfileRouter"/>
/// picks it (PR-kind → <c>pr-review</c>; issue-kind →
/// <c>IntegrationProfileRouter.IssueDefault</c>, default <c>implement</c>).
/// Per-connection <c>profileKey</c> in the settings jsonb wins.
/// Mirrors <c>ChatWorkerDefaults</c>.
/// </summary>
public sealed class IntegrationWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Integrations:Worker";

    /// <summary>Default profile key for issue-kind inbound items when no per-connection override is set.</summary>
    public string IssueDefaultProfileKey { get; init; } = "implement";

    /// <summary>Worker image (with digest) integrations-created items claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo integrations-created items claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
