using System.Data.Common;
using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Host.Runs;

/// <summary>
/// Host-side <see cref="ICancelRunPort"/>: the operator-initiated cancel
/// transitions any non-terminal run to <c>Cancelled</c>, bumps the run's
/// own <see cref="Run.Generation"/>, fences every currently
/// <see cref="WorkItemStatus.Running"/> work item under it (bumps each
/// item's <see cref="WorkItem.Generation"/>), and appends the journal
/// row in the same transaction. The fencing invalidates worker authority:
/// a worker still holding a pre-cancel lease gets the existing WS4
/// <c>work-item.not_owner</c> 409 on its next heartbeat / complete / fail
/// (no new response shape is introduced). The fenced item's lease fields
/// (<c>leased_by</c> / <c>lease_until</c> / <c>heartbeat_at</c>) and its
/// <c>status</c> are left intact — only <c>generation</c> and
/// <c>updated_at</c> change, so the existing lease reaper still reclaims
/// the item on its normal TTL/grace schedule.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
/// <param name="clock">Time source for the transition stamp and the journal row.</param>
public sealed class HostCancelRunAdapter(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock) : ICancelRunPort
{
    /// <inheritdoc />
    public async Task CancelAsync(RunId runId, string? reason, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("runs-cancel");

        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) ?? throw new ProviderNotFoundException(
                "run.not_found",
                $"run '{runId.Value}' not found");

        var candidate = run.Status;
        if (!RunTransitions.IsLegal(candidate, RunStatus.Cancelled))
        {
            throw new RunDecisionConflictException(
                candidate,
                RunStatus.Cancelled,
                "cancel");
        }

        var now = clock.GetUtcNow();
        await RunCancelSql.ApplyWithFencingAsync(
            db,
            runId,
            reason,
            candidate,
            now,
            cancellationToken);
    }
}

/// <summary>
/// Cancel + generation-fence raw SQL + bounded CAS retry — the host-local
/// counterpart of <c>WorkItemQueueSql</c> (engine-internal, a different
/// assembly). Status literals are the
/// PascalCase <see cref="WorkItemStatus"/> / <see cref="RunStatus"/> names
/// EF's <c>HasConversion&lt;string&gt;</c> stores, sourced via <c>nameof</c>
/// so a status rename fails the build instead of silently going stale. All
/// SQL references the per-module
/// <see cref="OrchestrationDatabase"/> constants so the queries find the
/// tables regardless of <c>search_path</c>. The two UPDATEs run inside one
/// transaction in **item-row-then-run-row** order — mirroring
/// <c>WorkItemOwnedTransition.ApplyAsync</c>'s complete/finalize
/// path so a concurrent cancel and a concurrent complete cannot deadlock
/// (transaction A would not hold the run row wanting item rows while
/// transaction B holds item rows wanting the run row).
/// </summary>
file static class RunCancelSql
{
    /// <summary>Bounded CAS retry budget. Five attempts is comfortably more than the
    /// pathological contention this path sees — the integration tests don't drive
    /// adversarial concurrency beyond a single racing peer.</summary>
    private const int MaxCancelAttempts = 5;

    /// <summary>Compiler-checked status name — see class remarks.</summary>
    private const string Running = nameof(WorkItemStatus.Running);

    /// <summary>Compiler-checked run status literal.</summary>
    private const string RunCancelled = nameof(RunStatus.Cancelled);

    /// <summary>Fence every currently-Running work item under @runId by bumping its
    /// generation in place. Lease columns and status are untouched (so the existing
    /// reaper still reclaims the item on TTL/grace). Safe to run every time — a
    /// no-op when nothing is Running.</summary>
    public const string FenceLiveItemsSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET generation = generation + 1, updated_at = @now "
        + "WHERE run_id = @runId AND status = '" + Running + "'";

    /// <summary>Guarded CAS that bumps the run's own generation alongside the status
    /// transition. 0 rows affected = someone else moved the run since the caller
    /// last observed it (concurrent claim-activated Queued→Running, or concurrent
    /// complete finalized it, or a concurrent cancel already won).</summary>
    public const string GuardedRunTransitionSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " "
        + "SET status = '" + RunCancelled + "', generation = generation + 1, updated_at = @now "
        + "WHERE id = @runId AND status = @fromStatus";

    /// <summary>Bounded CAS retry around the fence-and-cancel pair. On the first
    /// successful CAS the journal append commits and the method returns. On a
    /// 0-row CAS the transaction rolls back, the run is re-read, the legality
    /// table is checked — retry if still legal, throw
    /// <see cref="RunDecisionConflictException"/> otherwise or when the attempt
    /// budget is exhausted.</summary>
    /// <param name="db">Orchestration context — same scope as the adapter.</param>
    /// <param name="runId">Target run.</param>
    /// <param name="reason">Operator-supplied free-text reason (or null).</param>
    /// <param name="initialCandidate">Run's current status as observed by the caller.</param>
    /// <param name="now">Wall-clock stamp from the adapter's <see cref="TimeProvider"/>.</param>
    /// <param name="cancellationToken"></param>
    public static async Task ApplyWithFencingAsync(
        OrchestrationDbContext db,
        RunId runId,
        string? reason,
        RunStatus initialCandidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidate = initialCandidate;
        for (var attempt = 0; attempt < MaxCancelAttempts; attempt++)
        {
            var fresh = await TryCancelOnceAsync(db, runId, reason, candidate, now, cancellationToken);
            if (fresh is null)
            {
                return;
            }

            if (!RunTransitions.IsLegal(fresh.Value, RunStatus.Cancelled))
            {
                throw new RunDecisionConflictException(fresh.Value, RunStatus.Cancelled, "cancel");
            }

            candidate = fresh.Value;
        }

        throw new RunDecisionConflictException(candidate, RunStatus.Cancelled, "cancel");
    }

    /// <summary>One fence-and-cancel attempt inside its own transaction. Returns
    /// <c>null</c> on a successful commit (the journal row landed); returns the
    /// freshly-observed run status on a 0-row CAS so the outer loop can
    /// re-check legality and retry.</summary>
    /// <param name="db">Orchestration context.</param>
    /// <param name="runId">Target run.</param>
    /// <param name="reason">Optional operator reason.</param>
    /// <param name="fromStatus">Exact status the caller last observed — the CAS predicate.</param>
    /// <param name="now">Transition stamp.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<RunStatus?> TryCancelOnceAsync(
        OrchestrationDbContext db,
        RunId runId,
        string? reason,
        RunStatus fromStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Item-row-then-run-row lock order mirrors WorkItemQueueEf's
        // complete/finalize path — see the class remarks. Both UPDATEs run
        // on the transaction's own connection so they share the txn.
        await using (var fenceCommand = CreateFenceLiveItemsCommand(transaction.GetDbTransaction(), runId, now))
        {
            await fenceCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var runCommand = CreateGuardedRunTransitionCommand(transaction.GetDbTransaction(), runId, fromStatus, now);
        var rowsAffected = await runCommand.ExecuteNonQueryAsync(cancellationToken);
        if (rowsAffected == 0)
        {
            // Concurrent actor moved the run — abandon this transaction,
            // let the outer loop re-read and decide (retry if still
            // legal, conflict otherwise).
            await transaction.RollbackAsync(cancellationToken);
            return await db.Runs.AsNoTracking()
                .Where(r => r.Id == runId)
                .Select(r => r.Status)
                .FirstAsync(cancellationToken);
        }

        var payload = JsonSerializer.Serialize(
            new RunStatusChangedPayload(
                fromStatus.ToString(),
                RunStatus.Cancelled.ToString(),
                Actor: "operator",
                Reason: CancelRunReason.Normalize(reason)),
            JsonSerializerOptions.Web);
        db.RunEvents.Add(RunEvent.Create(runId, RunEventTypes.RunStatusChanged, payload, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>Creates a prepared fence-live-items command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="runId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateFenceLiveItemsCommand(DbTransaction transaction, RunId runId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = FenceLiveItemsSql;
        AddParameter(command, "@runId", runId.Value);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared guarded-run-transition command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="runId"></param>
    /// <param name="fromStatus"></param>
    /// <param name="now"></param>
    public static DbCommand CreateGuardedRunTransitionCommand(
        DbTransaction transaction,
        RunId runId,
        RunStatus fromStatus,
        DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = GuardedRunTransitionSql;
        AddParameter(command, "@runId", runId.Value);
        AddParameter(command, "@fromStatus", fromStatus.ToString());
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Adds one typed parameter (Npgsql infers uuid/timestamptz/text from the CLR value).</summary>
    /// <param name="command"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>
/// Reason-field normalisation for the cancel journal payload — isolated
/// so the adapter holds only the transition + persistence flow
/// (<c>code-shape.md</c> §1a).
/// </summary>
file static class CancelRunReason
{
    /// <summary>Strips a whitespace-only / null reason to <c>null</c>; the jsonb field is then omitted.</summary>
    /// <param name="reason">User-supplied reason.</param>
    public static string? Normalize(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

/// <summary>Run status-change payload — shared with engine-internal handlers that emit <c>run.status_changed</c>.</summary>
/// <param name="From">Source status (PascalCase).</param>
/// <param name="To">Target status (PascalCase).</param>
/// <param name="Actor">Operator verb or system consumer name.</param>
/// <param name="Reason">Optional human note (jsonb <c>null</c> when absent).</param>
internal sealed record RunStatusChangedPayload(string From, string To, string Actor, string? Reason);
