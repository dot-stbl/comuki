using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Host-composed <see cref="WorkSubscriberBase"/> that polls the
/// engine outbox for <see cref="OrchestrationEventTypes.RunTerminatedV1"/>
/// AND <see cref="OrchestrationEventTypes.RunCancelledV1"/>,
/// advances the WorkTaskAttempt row to terminal, and clears the
/// parent WorkTask's <c>activeAttemptId</c>. The mirror-dedupe key
/// <c>work.task.{taskId}:terminal:{attemptOrdinal}</c> is the
/// Work-side equivalent of the WS9 admission-claim pattern: a
/// second delivery for the same attempt finds the existing
/// <c>work.inbox_receipts</c> row and no-ops.
/// </summary>
public sealed class WorkIngestRunTerminalSubscriber(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WorkIngestRunTerminalSubscriber> logger)
    : WorkSubscriberBase(scopeFactory, clock, logger)
{
    /// <inheritdoc />
    public override string Name => "work-ingest-run-terminal-subscriber";

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GetSubscribedTypes()
    {
        return subscribedTypes;
    }

    /// <inheritdoc />
    protected override string WatermarkKey => WatermarkKeyInternal;

    /// <inheritdoc />
    protected override async Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var payload = EngineTerminalPayload.TryParse(row.Payload);
        if (payload is null || payload.RunId == Guid.Empty)
        {
            return false;
        }

        var db = serviceProvider.GetRequiredService<Persistence.WorkDbContext>();
        var inbox = serviceProvider.GetRequiredService<IWorkInbox>();
        var store = serviceProvider.GetRequiredService<IWorkTaskStore>();

        var runId = new RunId(payload.RunId);

        // The mirror dedupe is the Work-side inbox_receipts row
        // keyed on the per-attempt envelope. A re-delivery of the
        // same terminal event for the same attempt no-ops.
        var attempt = await db.WorkTaskAttempts
            .AsNoTracking()
            .Where(attempt => attempt.RunId == runId.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            // The Work side has no record of this attempt — likely
            // a pre-existing Run whose attempt ledger predates this
            // change. The backfill (task 6) covers legacy Runs. The
            // watermark still advances so the cycle doesn't loop.
            return false;
        }

        var dedupeKey = $"work.task.{attempt.TaskId}:terminal:{attempt.AttemptOrdinal}";
        if (!await inbox.TryClaimAsync(dedupeKey, cancellationToken))
        {
            // Duplicate delivery — same attempt, same terminal,
            // second time around. Skip without re-stamping.
            return false;
        }

        // Three distinct writes commit in this same scope, none
        // atomic across each other: the dedupe claim (the inbox
        // row above), the attempt-row terminal stamp, and the
        // WorkTask attempt-cleared save. A crash between any two
        // leaves dedupe distinct from the attempt row, and a
        // retried event finds the attempt row still not stamped
        // — at-most-once AFTER the dedupe claim wins, not
        // exactly-once. The processing bias is forward-only
        // (the attempt row's terminal stamp never moves
        // backward): the caller may observe a stamp with no
        // dedupe-on-disk row, but never an unstamped attempt that
        // arrived through the claim. The watermark advance at the
        // cycle's end serialises the read side.
        var tracked = await db.WorkTaskAttempts
            .FirstOrDefaultAsync(a => a.Id == attempt.Id, cancellationToken);
        if (tracked is { TerminalStatus: null })
        {
            var terminalStatus = payload.Status
                ?? (row.Type == OrchestrationEventTypes.RunCancelledV1 ? "Cancelled" : "Terminal");
            var terminalAt = Clock.GetUtcNow();
            db.WorkTaskAttempts.Remove(tracked);
            db.WorkTaskAttempts.Add(tracked with { TerminalStatus = terminalStatus, TerminalAt = terminalAt });
        }

        await db.SaveChangesAsync(cancellationToken);

        // Clear the WorkTask's active attempt. CompleteAttempt
        // requires the run id to match the active one; on a
        // double-delivery race the second call no-ops via the
        // dedupe above, so this branch only runs on first ingest.
        var taskId = new WorkTaskId(attempt.TaskId);
        var task = await store.FindAsync(taskId, cancellationToken);
        if (task is { ActiveAttemptId: { } activeId } && activeId == runId)
        {
            task.CompleteAttempt(activeId, Clock.GetUtcNow());
            await store.SaveAsync(task, cancellationToken);
        }

        return true;
    }

    /// <summary>The two engine-emitted terminal event types — joined under one subscriber for share-everything dedupe.</summary>
    private static readonly IReadOnlyCollection<string> subscribedTypes =
        [OrchestrationEventTypes.RunTerminatedV1, OrchestrationEventTypes.RunCancelledV1];

    /// <summary>The watermark key — a single counter covers both subscribed types (they share the same Work-side dedupe ledger).</summary>
    private const string WatermarkKeyInternal = "orchestration.run.terminal.v1";
}
