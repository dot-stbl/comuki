namespace Comuki.Host.Intake;

/// <summary>
/// Worker launch defaults for intake-created tickets: the claim labels
/// the single queued work item of an intake run carries. Mirrors
/// <c>ChatWorkerDefaults</c> — v1 reads them from configuration because
/// profiles carry no image metadata yet.
/// </summary>
public sealed class IntakeWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Intake:Worker";

    /// <summary>Profile key intake-created items claim on.</summary>
    public string ProfileKey { get; init; } = "general";

    /// <summary>Worker image (with digest) intake-created items claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo intake-created items claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
