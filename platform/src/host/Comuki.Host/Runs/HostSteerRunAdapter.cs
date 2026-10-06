using System.Data.Common;
using System.Text.Json;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Projects;
using Comuki.Host.Workers.Grpc;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Harness;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Runs;

/// <summary>
/// Host-side <see cref="ISteerRunPort"/>: the operator-initiated steer
/// (add-orchestra §1 — Baton). The flow:
/// <list type="number">
///   <item>Resolves <c>runId</c> via <see cref="IExecutionIdResolver"/> —
///   the shared seam the run-cancel endpoint also uses (the spec's
///   "RunId-to-ExecutionId resolver is shared" requirement).</item>
///   <item>Refuses terminal / finalising runs with a 409
///   <c>run.not_running</c> — the follow-up never lands on a dead run.</item>
///   <item>Reads the live execution's harness through
///   <see cref="IRunHarnessResolver"/> and reads
///   <see cref="HarnessCapabilities.LiveSession"/>:
///   <list type="bullet">
///     <item><c>LiveSession = true</c> — Phase 1c canonical path. The
///     operator's steer text rides the
///     <see cref="IWorkerCommandPipe"/> bidi channel as a
///     <see cref="TurnInput"/> command. The response is
///     <c>{ delivered: true | false }</c>; a <c>false</c> delivery
///     is non-fatal and the caller may retry
///     (<c>specs/session/spec.md</c> scenarios "Steer lands on a
///     live session" / "Steer misses without a live stream").</item>
///     <item><c>LiveSession = false</c> — Phase 1a canonical path.
///     Stages a follow-up <see cref="WorkItem"/> on the same
///     <see cref="Run"/>, carrying the operator's steer text as
///     the brief (the cowork 11.1 fallback "stage a new research
///     WorkItem"). The response carries the same
///     <c>delivered: true</c> shape with a <c>followUpWorkItemId</c>.</item>
///   </list></item>
/// </list>
/// <para>
/// The bidi path lands in Phase 1c; the follow-up path is preserved
/// for harnesses that declare <c>LiveSession = false</c> (today's
/// runtime, and <c>TestFakeHarness</c> with the false case) — both
/// branches are reachable through the same seam. The
/// <see cref="IExecutionIdResolver"/> outcome is informational on the
/// bidi path (a non-null <c>WorkerId</c> means a worker is currently
/// driving the run; <c>null</c> means the reaper will reclaim the
/// lease) and the missing-worker case answers
/// <c>{ delivered: false }</c> per the spec.
/// </para>
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
/// <param name="resolver">Shared <c>runId → WorkerId</c> resolver.</param>
/// <param name="harnessResolver">Resolver for the live execution's
/// <see cref="IHarness"/> — the seam the <see cref="HarnessCapabilities"/>
/// read rides on (Phase 1c).</param>
/// <param name="commandPipe">Bidi command channel to the worker
/// (Phase 1c — the <see cref="TurnInput"/>-on-the-gRPC-stream
/// surface). The <c>TrySend*</c> shape returns <c>false</c> on a
/// missing live stream (miss, not error).</param>
/// <param name="defaults">Follow-up worker image / profiles-ref.</param>
/// <param name="buildInformation">Build identity — pins the follow-up image to the running version.</param>
/// <param name="clock">Wall-clock source for the follow-up stamp.</param>
/// <param name="projects">Projects module port — stamps <c>Project.EnvClass</c> on the follow-up.</param>
/// <param name="logger">Structured logger.</param>
public sealed class HostSteerRunAdapter(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor,
    IExecutionIdResolver resolver,
    IRunHarnessResolver harnessResolver,
    IWorkerCommandPipe commandPipe,
    IOptions<SteeringWorkerDefaults> defaults,
    ComukiBuildInformation buildInformation,
    TimeProvider clock,
    IProjectStore projects,
    ILogger<HostSteerRunAdapter> logger) : ISteerRunPort
{
    /// <inheritdoc />
    public async Task<SteerRunResult> SteerAsync(
        RunId runId,
        string text,
        CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("runs-steer");

        var run = await db.Runs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new ProviderNotFoundException(
                "run.not_found",
                $"run '{runId.Value}' not found");

        if (RunSteerStatusGuard.IsTerminalOrFinalising(run.Status))
        {
            throw new RunNotRunningForSteerException(run.Status);
        }

        var liveWorker = await resolver.ResolveAsync(runId, cancellationToken);
        var liveHarness = await harnessResolver.ResolveAsync(runId, cancellationToken);

        // Phase 1c canonical path — the harness declares
        // Capabilities.LiveSession = true, the bidi channel carries
        // the operator's turn as a TurnInput command, the worker
        // forwards it to the harness's session transport, and the
        // harness's response replaces the accumulated text on the
        // next run summary (per specs/session/spec.md Requirement
        // "TurnInput is the authoritative session turn", scenario
        // "Authoritative turn replaces accumulated text"). A
        // TrySendTurnInput miss returns delivered:false — the
        // worker has no live stream (the reaper owns the lease, or
        // the worker dropped the gRPC stream between lease-mint and
        // steer); the caller may retry.
        if (liveHarness is { Capabilities.LiveSession: true })
        {
            return await SteerViaTurnInputAsync(
                liveWorker,
                runId,
                text,
                cancellationToken);
        }

        // Phase 1a canonical path — no LiveSession capability, no bidi
        // delivery. Stage the follow-up regardless of whether a worker
        // is currently driving the run; the steer must survive a worker
        // restart and the reaper reclaims the lease. Phase 1c adds the
        // bidi path on top of this same seam.
        //
        // TOCTOU guard: re-read the run row inside the same transaction
        // and re-check the terminal-status predicate before the
        // INSERT lands. A concurrent cancel / finalize between the
        // initial read and the stage will leave the row
        // in a terminal status we refuse to follow-up against, the
        // transaction rolls back, and the caller sees a typed 409.
        // On the in-memory store (unit tests) the transaction is
        // unsupported, but the re-read-after-update row read-then-act
        // ordering already covers the test path.
        var followUp = await StageFollowUpAsync(run, text, cancellationToken);

        logger.LogInformation(
            "Steer on run {RunId} staged follow-up work item {WorkItemId} (live worker was {WorkerState}, harness live-session={LiveSession})",
            runId.Value,
            followUp.Id,
            liveWorker is null ? "absent" : "present",
            liveHarness is { Capabilities.LiveSession: true });

        return new SteerRunResult(true, followUp.Id);
    }

    /// <summary>
    /// Sends the operator's turn through the bidi <see cref="TurnInput"/>
    /// command and returns the wire-shape <see cref="SteerRunResult"/>.
    /// A worker without a live stream answers
    /// <c>{ delivered: false }</c>; a live worker answers
    /// <c>{ delivered: true }</c> with no follow-up
    /// <see cref="WorkItem"/> id. The spec's
    /// <c>code = session.livesession_unavailable</c> 409 path is
    /// reserved for the explicit, intentional no-LiveSession
    /// declaration that Phase 8 / Instrument brings; today's
    /// no-LiveSession runtime reaches the follow-up branch above
    /// by default, not this one (the harness resolver returns
    /// <c>null</c> when the run has no Running work item).
    /// </summary>
    /// <param name="liveWorker">Worker the resolver returned; <c>null</c>
    /// when the reaper has reclaimed the lease.</param>
    /// <param name="runId">Run being steered (already known to be non-terminal).</param>
    /// <param name="text">Operator's steer text.</param>
    /// <param name="cancellationToken">Token propagated to the bidi send.</param>
    private Task<SteerRunResult> SteerViaTurnInputAsync(
        WorkerId? liveWorker,
        RunId runId,
        string text,
        CancellationToken cancellationToken)
    {
        if (liveWorker is not { } workerId)
        {
            logger.LogInformation(
                "Steer on run {RunId} declined: harness declares LiveSession but the resolver saw no live worker (reaper owns the lease)",
                runId.Value);
            return Task.FromResult(new SteerRunResult(Delivered: false, FollowUpWorkItemId: null));
        }

        var delivered = commandPipe.TrySendTurnInput(
            workerId,
            new TurnInput
            {
                Text = text,
                Role = "user",
                Metadata = [],
            });

        logger.LogInformation(
            "Steer on run {RunId} tried bidi TurnInput on worker {WorkerId}: delivered={Delivered}",
            runId.Value,
            workerId.Value,
            delivered);

        return Task.FromResult(new SteerRunResult(Delivered: delivered, FollowUpWorkItemId: null));
    }

    /// <summary>
    /// Stages the follow-up <see cref="WorkItem"/>: the same profile
    /// and env class as the live item (so a worker of the same kind
    /// claims it), the operator's steer text as the brief, and
    /// <see cref="WorkItemStatus.Queued"/> (the follow-up has no DAG
    /// edges — a fresh worker claims it as soon as the in-flight one
    /// is fenced by cancel or reaped by the lease policy). The whole
    /// read-check-insert sequence runs in one transaction; on the
    /// relational path the run row is locked with
    /// <c>SELECT ... FOR UPDATE</c> so a concurrent cancel / finalize
    /// between the initial read and the INSERT serializes against
    /// the lock and rolls the steer transaction back with a typed
    /// 409 <c>run.not_running</c> — the same seam the cancel path
    /// uses. On the in-memory store (unit tests) the transaction is
    /// unsupported, but the read-then-act row-level check the
    /// <c>SELECT FOR UPDATE</c> runs is the same one the in-memory
    /// branch falls back to (a fresh re-read inside the run-followup
    /// path), so both branches share the predicate and the
    /// refuse-on-terminal-status rule.
    /// </summary>
    /// <param name="run">Run the follow-up attaches to (snapshot from the initial read).</param>
    /// <param name="text">The operator's steer text.</param>
    /// <param name="cancellationToken">Token propagated to <see cref="EnvClassResolver.ResolveAsync"/> and the EF SaveChanges / commit.</param>
    private async Task<WorkItem> StageFollowUpAsync(Run run, string text, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var image = WorkerImagePinning.Resolve(defaults.Value.Image, buildInformation);
        var envClass = await EnvClassResolver.ResolveAsync(projects, run.ProjectId, "runs-steer", cancellationToken);

        var workItem = WorkItem.Create(
            run.Id,
            defaults.Value.ProfileKey,
            image,
            envClass,
            defaults.Value.ProfilesRef,
            SteerFollowUpBrief.ToJson(text),
            WorkItemStatus.Queued,
            now);

        db.WorkItems.Add(workItem);

        // A run_events row gives the dashboard timeline a real
        // record of the steer (the follow-up itself is a Queued
        // work item, not a status change; without the event, the
        // operator's steer vanishes from the journal the moment the
        // follow-up is claimed).
        db.RunEvents.Add(RunEvent.Create(
            run.Id,
            RunEventTypes.RunSteerFollowUpQueued,
            JsonSerializer.Serialize(
                new RunSteerFollowUpPayload(text, workItem.Id),
                JsonSerializerOptions.Web),
            now));

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // The relational path locks the run row with SELECT ... FOR UPDATE
        // so a concurrent cancel / finalize between the initial read
        // and the INSERT serializes against the same row lock the
        // cancel path acquires. The predicate fires again on the
        // re-read; a terminal status raises a typed 409 and the
        // transaction rolls back. The in-memory branch (unit tests
        // use the InMemory provider) can't host a transaction, so
        // it falls back to a no-tracking re-read — the predicate is
        // the same one, the rejection path is the same, and the test
        // surface (in-memory, single-call) does not exercise
        // cross-call concurrency.
        RunStatus observedStatus;
        if (db.Database.IsRelational())
        {
            observedStatus = await SteerSql.LockRunStatusAsync(transaction!, run.Id, cancellationToken);
        }
        else
        {
            var fresh = await db.Runs
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == run.Id, cancellationToken) ?? throw new RunNotRunningForSteerException(run.Status);
            observedStatus = fresh.Status;
        }

        if (RunSteerStatusGuard.IsTerminalOrFinalising(observedStatus))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw new RunNotRunningForSteerException(observedStatus);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return workItem;
    }
}

/// <summary>
/// Port for the operator-initiated steer (add-orchestra §1 — Baton).
/// Implemented in the host composition root over the orchestration
/// context; modules never reference the engine. The result carries
/// both the delivery flag and the follow-up work item id so the
/// controller can pass through the typed 202 body without a second
/// read.
/// </summary>
public interface ISteerRunPort
{
    /// <summary>
    /// Steers the run. The follow-up WorkItem is staged on the no-LiveSession
    /// runtime; the delivery flag reports whether the runtime had a live
    /// channel to deliver to (Phase 1c only — today every steer returns
    /// <c>Delivered = true</c> because the follow-up is the canonical
    /// outcome).
    /// </summary>
    /// <param name="runId">Run to steer.</param>
    /// <param name="text">The operator's steer text.</param>
    /// <param name="cancellationToken">Token propagated to the resolver, the journal, and the FOR UPDATE / SaveChanges / commit.</param>
    /// <exception cref="ProviderNotFoundException">The run does not exist or is out of scope.</exception>
    /// <exception cref="RunDecisionConflictException">The run is in a terminal status.</exception>
    public Task<SteerRunResult> SteerAsync(
        RunId runId,
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The port's result. <see cref="Delivered"/> is the wire-level
/// <c>delivered</c> field; <see cref="FollowUpWorkItemId"/> is set on
/// the no-LiveSession runtime (Phase 1a) and absent when the runtime
/// delivered through the bidi channel instead (Phase 1c, when the
/// <c>Capabilities.LiveSession</c> flag lands).
/// </summary>
/// <param name="Delivered">The wire-level <c>delivered</c> flag.</param>
/// <param name="FollowUpWorkItemId">The follow-up work item id (Phase 1a) or <c>null</c> (Phase 1c).</param>
public sealed record SteerRunResult(bool Delivered, Guid? FollowUpWorkItemId);

/// <summary>
/// Pure-function guard for the steer refusal path: terminal-status
/// predicate. Extracted from the adapter so the test surface exercises
/// the predicate directly without going through the EF Core loop.
/// </summary>
file static class RunSteerStatusGuard
{
    /// <summary>
    /// True for terminal statuses (<see cref="RunStatus.Succeeded"/>,
    /// <see cref="RunStatus.Failed"/>, <see cref="RunStatus.Cancelled"/>)
    /// — the steer refusal path. The escalation sweeper is a separate
    /// state and a non-finalising one; the run's own
    /// <c>RunTransitions</c> is the source of truth and a steer
    /// against any non-terminal status is legal.
    /// </summary>
    /// <param name="status">Run status to test.</param>
    public static bool IsTerminalOrFinalising(RunStatus status)
    {
        return status == RunStatus.Succeeded
            || status == RunStatus.Failed
            || status == RunStatus.Cancelled;
    }
}

/// <summary>Brief payload the follow-up worker reads.</summary>
file static class SteerFollowUpBrief
{
    /// <summary>JSON shape: <c>{"Goal": "...", "Source": "operator-steer"}</c>. The <c>Source</c> discriminator lets workers distinguish operator steers from chat/intake briefs without parsing free text.</summary>
    /// <param name="text">The operator's steer text.</param>
    public static string ToJson(string text)
    {
        return JsonSerializer.Serialize(new SteerFollowUpGoal(text, "operator-steer"), JsonSerializerOptions.Web);
    }
}

/// <summary>Worker goal shape for an operator steer.</summary>
/// <param name="Goal">The operator's steer text (verbatim).</param>
/// <param name="Source">Stable <c>"operator-steer"</c> discriminator.</param>
file sealed record SteerFollowUpGoal(string Goal, string Source);

/// <summary>Run-events payload for the steer follow-up.</summary>
/// <param name="Text">The operator's steer text.</param>
/// <param name="FollowUpWorkItemId">The id of the queued follow-up work item.</param>
internal sealed record RunSteerFollowUpPayload(string Text, Guid FollowUpWorkItemId);

/// <summary>
/// Steer-side counterpart of <c>HostCancelRunAdapter.RunCancelSql</c>:
/// locks the run row with <c>SELECT ... FOR UPDATE</c> on the relational path
/// so a concurrent cancel / finalize that flips the run to a
/// terminal status between the adapter's initial read and the
/// follow-up insert serializes against the same row lock the
/// cancel path acquires. Status literals are the
/// PascalCase <see cref="RunStatus"/> names EF's
/// <c>HasConversion&lt;string&gt;</c> stores, sourced via
/// <c>nameof</c> so a status rename fails the build instead of
/// silently going stale. The helper runs on the transaction's own
/// connection so the lock survives the commit boundary.
/// </summary>
file static class SteerSql
{
    /// <summary>Locks the @runId row for the rest of the transaction and returns
    /// its current <see cref="RunStatus"/> — the predicate the steer
    /// refusal path runs against.</summary>
    public const string LockRunStatusSql =
        "SELECT status FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " "
        + "WHERE id = @runId "
        + "FOR UPDATE";

    /// <summary>Creates a prepared lock-runs-row command on the transaction's
    /// connection. The lock is held for the rest of the transaction; the
    /// outer <c>SteerAsync</c> branch is responsible for commit / rollback.</summary>
    /// <param name="transaction">Live transaction whose connection the command runs on.</param>
    /// <param name="runId">Run the steer is targeting.</param>
    public static DbCommand CreateLockRunStatusCommand(DbTransaction transaction, RunId runId)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = LockRunStatusSql;
        AddParameter(command, "@runId", runId.Value);
        return command;
    }

    /// <summary>Adds one typed parameter (Npgsql infers uuid from the CLR value).</summary>
    /// <param name="command">Command the parameter is added to.</param>
    /// <param name="name">Parameter name including the <c>@</c> prefix.</param>
    /// <param name="value">Parameter value (uuid / text / timestamptz).</param>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Locks the run row and returns its <see cref="RunStatus"/>.</summary>
    /// <param name="transaction">Live transaction the lock is held in.</param>
    /// <param name="runId">Run the steer is targeting.</param>
    /// <param name="cancellationToken">Token forwarded to <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/>.</param>
    public static async Task<RunStatus> LockRunStatusAsync(
        IDbContextTransaction transaction,
        RunId runId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateLockRunStatusCommand(transaction.GetDbTransaction(), runId);
        var raw = await command.ExecuteScalarAsync(cancellationToken);
        // boundary: Npgsql materialises the varchar column as a CLR string
        var status = (raw as string) ?? raw?.ToString()
            ?? throw new InvalidOperationException("runs row returned a null status");
        return RunStatus.FromWire(status);
    }
}
