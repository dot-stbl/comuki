namespace Comuki.Modules.Scheduler.Application.Options;

/// <summary>
/// Background service tunables for the scheduler dispatcher.
/// </summary>
public sealed class SchedulerOptions
{
    /// <summary>Configuration section the host binds from.</summary>
    public const string SectionName = "Scheduler";

    /// <summary>Polling interval for the dispatcher — how often we look for due jobs.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-cycle batch cap; the dispatcher never claims more than this in one transaction.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Sentry side-channel options. Defaults to an empty stub so the
    /// <c>Scheduler:Sentry</c> section can be omitted from configuration
    /// — the observer short-circuits on a null DSN anyway.
    /// </summary>
    public SchedulerSentryOptions Sentry { get; init; } = new();
}
