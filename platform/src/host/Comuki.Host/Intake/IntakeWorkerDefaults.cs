namespace Comuki.Host.Intake;

/// <summary>
/// Worker launch defaults for intake-created tickets: the worker image
/// and the pinned profiles-ref. The worker <c>profileKey</c> is no
/// longer hardcoded — <see cref="Modules.Intake.Application.Ports.Admission.IIntakeProfileRouter"/>
/// picks it (PR-kind → <c>pr-review</c>; issue-kind →
/// <c>IntakeProfileRouter.IssueDefault</c>, default <c>general</c>).
/// Per-connection <c>profileKey</c> in the settings jsonb wins.
/// Mirrors <c>ChatWorkerDefaults</c>.
/// </summary>
public sealed class IntakeWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Intake:Worker";

    /// <summary>Default profile key for issue-kind intake tickets when no per-connection override is set.</summary>
    public string IssueDefaultProfileKey { get; init; } = "general";

    /// <summary>Worker image (with digest) intake-created items claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo intake-created items claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
