using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;

/// <summary>
/// Passive autonomy ratchet on the Escalated state. Each pass queries
/// <see cref="RunStatus.Escalated"/> runs whose <c>UpdatedAt</c> is older
/// than the configured idle window, transitions each to
/// <see cref="RunStatus.Cancelled"/> and journals one
/// <see cref="RunEventTypes.RunEscalationTimeout"/> audit row in a single
/// <c>SaveChanges</c>. The query is bounded — only the columns needed for
/// the cutoff check — and the transitions are guarded by a re-check of
/// <see cref="Run.Status"/> before the aggregate mutator runs (a second
/// process may have re-queued the run between the SELECT and the
/// transition). The sweeper expects to run inside an
/// <c>AsSystem("escalation-timeout-sweeper")</c> scope (same contract as
/// <c>LeaseReaper</c>): the orchestration subject-scope filter would
/// otherwise hide the stale rows.
/// </summary>
/// <param name="db"></param>
/// <param name="clock"></param>
/// <param name="options"></param>
public sealed class EscalationTimeoutSweeper(
    OrchestrationDbContext db,
    TimeProvider clock,
    IOptions<EscalationTimeoutOptions> options)
{
    /// <summary>Runs one sweep; safe to call repeatedly and concurrently within a single replica.</summary>
    /// <param name="cancellationToken"></param>
    public async Task<EscalationTimeoutSwept> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.Subtract(options.Value.EscalationTimeout);

        var staleIds = await db.Runs
            .AsNoTracking()
            .Where(run => run.Status == RunStatus.Escalated && run.UpdatedAt < cutoff)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
        {
            return new EscalationTimeoutSwept(0, []);
        }

        var archived = new List<Shared.Kernel.Ids.RunId>(staleIds.Count);
        var sweepNow = clock.GetUtcNow();

        foreach (var id in staleIds)
        {
            var run = await db.Runs.FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
            if (run is null)
            {
                continue;
            }

            if (run.Status != RunStatus.Escalated)
            {
                continue;
            }

            var ageSeconds = (sweepNow - run.UpdatedAt).TotalSeconds;
            run.TransitionTo(RunStatus.Cancelled, sweepNow);
            db.RunEvents.Add(RunEvent.Create(
                run.Id,
                RunEventTypes.RunEscalationTimeout,
                EscalationTimeoutPayloads.EscalationTimeout(
                    run.Id,
                    nameof(RunStatus.Escalated),
                    nameof(RunStatus.Cancelled),
                    ageSeconds),
                sweepNow));
            archived.Add(run.Id);
        }

        if (archived.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new EscalationTimeoutSwept(archived.Count, archived);
    }
}
