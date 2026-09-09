using System.Text.Json;
using Comuki.Modules.Scheduler.Application.Observers;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Scheduler.Infrastructure.Observers;

/// <summary>
/// Default <see cref="ISchedulerObserver"/>: appends a
/// <c>scheduler.job_fired</c> entry to the orchestration
/// <c>run_events</c> journal so downstream subscribers (realtime hub,
/// audit, future chat notifier) see the fire alongside status changes
/// and worker reports. The observer is registered ahead of the Sentry
/// one so a transient journal failure is logged before the Sentry
/// capture runs; either failure must not abort the fire path itself
/// (the dispatcher catches observer exceptions per observer).
/// <para>
/// The observer stays Singleton: <see cref="IEnumerable{ISchedulerObserver}"/>
/// is captured into the dispatcher's Singleton ctor, so a Scoped
/// observer would be instantiated once at boot and hold the dispatcher's
/// root-scope <see cref="IRunJournal"/> forever. Instead, the observer
/// takes <see cref="IServiceScopeFactory"/> and opens its own scope per
/// <see cref="OnJobFiredAsync"/> call — the journal append sees a fresh
/// DbContext, the observer itself carries no per-call state, and the
/// captive-dependency problem disappears without disturbing the
/// dispatcher's observer fan-out.
/// </para>
/// </summary>
/// <param name="scopeFactory">Scope factory used to resolve a per-call <see cref="IRunJournal"/>.</param>
public sealed class JournalSchedulerObserver(IServiceScopeFactory scopeFactory) : ISchedulerObserver
{
    /// <summary>The journal type the observer writes.</summary>
    public const string EventType = "scheduler.job_fired";

    /// <inheritdoc />
    public async Task OnJobFiredAsync(
        ScheduledJobId jobId,
        ProjectId projectId,
        string profileKey,
        RunId runId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default)
    {
        var payload = new SchedulerJobFiredPayload(
            JobId: jobId.Value,
            ProjectId: projectId.Value,
            ProfileKey: profileKey,
            FiredAt: firedAt);

        var entry = new RunEventEntry(
            Id: Guid.CreateVersion7(),
            RunId: runId,
            Type: EventType,
            PayloadJson: JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            OccurredAt: firedAt);

        // Per-call scope: IRunJournal is Scoped (wraps OrchestrationDbContext),
        // and the observer itself must not hold it across fires — different
        // fires are independent DbContext units. The factory is the standard
        // captive-dependency escape hatch documented in di-lifetimes.md §5.
        await using var scope = scopeFactory.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        await journal.AppendAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Wire-format shape of the <c>scheduler.job_fired</c> payload. Open set —
    /// downstream consumers match on the event type and read the documented
    /// fields. ProfileKey is included so a subscriber can route the event
    /// to the right control-plane profile team without joining back to
    /// <c>scheduled_jobs.jobs</c>.
    /// </summary>
    /// <param name="JobId"></param>
    /// <param name="ProjectId"></param>
    /// <param name="ProfileKey"></param>
    /// <param name="FiredAt"></param>
    private sealed record SchedulerJobFiredPayload(
        Guid JobId,
        Guid ProjectId,
        string ProfileKey,
        DateTimeOffset FiredAt);
}
