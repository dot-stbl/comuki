namespace Comuki.Host.Scheduler;

/// <summary>
/// Worker launch defaults for cron-scheduled runs: the worker image
/// and pinned profiles-ref every dispatched job claims on. v1 reads
/// them from configuration because profiles carry no image metadata
/// yet — resolving image / ref per profile from the project's git
/// settings is a documented follow-up. Mirrors
/// <c>ChatWorkerDefaults</c> / <c>IntakeWorkerDefaults</c>.
/// </summary>
public sealed class SchedulerWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Scheduler:Worker";

    /// <summary>Worker image (with digest) scheduler-dispatched items claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo scheduler-dispatched items claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
