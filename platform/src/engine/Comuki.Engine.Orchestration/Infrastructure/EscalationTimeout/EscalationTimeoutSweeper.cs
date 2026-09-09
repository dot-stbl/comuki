using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;

/// <summary>
/// Passive autonomy ratchet on the Escalated state. Each pass runs one
/// guarded <c>UPDATE ... WHERE status = 'Escalated' AND updated_at &lt; cutoff
/// RETURNING</c> inside a transaction, then journals one
/// <see cref="RunEventTypes.RunEscalationTimeout"/> row per archived run in
/// a single <c>SaveChanges</c>. The re-check guard that the previous
/// SELECT-then-<c>FirstOrDefaultAsync</c>-per-id pattern needed is now
/// baked into the WHERE — a concurrent operator who re-queues a row
/// between the sweeper opening and the statement commits their update
/// first, the row's status is no longer <c>Escalated</c>, and our
/// predicate no longer matches. K stale rows = K rows touched + 1 journal
/// batch + 1 transaction commit, regardless of K. The sweeper expects
/// to run inside an <c>AsSystem("escalation-timeout-sweeper")</c> scope
/// (same contract as <c>LeaseReaper</c>): the orchestration
/// subject-scope filter would otherwise hide the stale rows.
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

        var archived = new List<ArchivedEscalatedRun>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using (var command = EscalationTimeoutSql.CreateArchiveCommand(
            transaction.GetDbTransaction(), cutoff, now))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                archived.Add(new ArchivedEscalatedRun(
                    new RunId(reader.GetGuid(0)),
                    reader.GetFieldValue<DateTimeOffset>(1)));
            }
        }

        if (archived.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new EscalationTimeoutSwept(0, []);
        }

        foreach (var row in archived)
        {
            var ageSeconds = (now - row.OldUpdatedAt).TotalSeconds;
            db.RunEvents.Add(RunEvent.Create(
                row.RunId,
                RunEventTypes.RunEscalationTimeout,
                EscalationTimeoutPayloads.EscalationTimeout(
                    row.RunId,
                    nameof(RunStatus.Escalated),
                    nameof(RunStatus.Cancelled),
                    ageSeconds),
                now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new EscalationTimeoutSwept(
            archived.Count,
            [.. archived.Select(static row => row.RunId)]);
    }
}

/// <summary>
/// One row returned by the archive UPDATE: the run id and the
/// <c>updated_at</c> value at the moment of the archive, used to compute
/// the journal <c>ageSeconds</c>.
/// </summary>
/// <param name="RunId"></param>
/// <param name="OldUpdatedAt"></param>
file sealed record ArchivedEscalatedRun(RunId RunId, DateTimeOffset OldUpdatedAt);
