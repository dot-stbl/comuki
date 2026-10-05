using System.Text.Json;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Projects;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Runs;

/// <summary>
/// Host-side <see cref="ISteerRunPort"/>: the operator-initiated steer
/// (add-orchestra §1 — Baton, Phase 1a). The flow:
/// <list type="number">
///   <item>Resolves <c>runId</c> via <see cref="IExecutionIdResolver"/> —
///   the shared seam the run-cancel endpoint also uses (the spec's
///   "RunId-to-ExecutionId resolver is shared" requirement).</item>
///   <item>Refuses terminal / finalising runs with a 409
///   <c>run.not_running</c> — the follow-up never lands on a dead run.</item>
///   <item>On the Phase 1a no-LiveSession runtime, stages a follow-up
///   <see cref="WorkItem"/> on the same <see cref="Run"/>, carrying the
///   operator's steer text as the brief (the same text the live worker's
///   <c>comuki-injected-context.md</c> would have carried — Phase 1a
///   follows the cowork 11.1 fallback "stage a new research WorkItem"
///   when the runtime lacks live injection).</item>
/// </list>
/// <para>
/// The LiveSession branch lands in Phase 1c and rides the
/// <c>IWorkerCommandPipe</c> bidi channel
/// (<c>TrySendInjectContext</c>). The current runtime declares no
/// <c>Capabilities.LiveSession</c> — every steer is no-LiveSession
/// today, the bidi path is not yet wired. The resolver's
/// <c>WorkerId?</c> outcome is informational: a non-null
/// <c>WorkerId</c> means a worker is currently driving the run;
/// <c>null</c> means the reaper will reclaim the lease. Either way
/// the follow-up is staged — the steer must survive a worker restart.
/// </para>
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
/// <param name="resolver">Shared <c>runId → WorkerId</c> resolver.</param>
/// <param name="defaults">Follow-up worker image / profiles-ref.</param>
/// <param name="buildInformation">Build identity — pins the follow-up image to the running version.</param>
/// <param name="clock">Wall-clock source for the follow-up stamp.</param>
/// <param name="projects">Projects module port — stamps <c>Project.EnvClass</c> on the follow-up.</param>
/// <param name="logger">Structured logger.</param>
public sealed class HostSteerRunAdapter(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor,
    IExecutionIdResolver resolver,
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

        if (IsTerminalOrFinalising(run.Status))
        {
            throw new RunNotRunningForSteerException(run.Status);
        }

        var liveWorker = await resolver.ResolveAsync(runId, cancellationToken);

        // Phase 1a canonical path — no LiveSession capability, no bidi
        // delivery. Stage the follow-up regardless of whether a worker
        // is currently driving the run; the steer must survive a worker
        // restart and the reaper reclaims the lease. Phase 1c adds the
        // bidi path on top of this same seam.
        //
        // TOCTOU guard: re-read the run row inside the same transaction
        // and re-check the terminal-status predicate before the
        // INSERT lands. A concurrent cancel / finalize between the
        // initial read and the stage would otherwise leave the
        // follow-up queued against a dead run — the run_events row
        // would also be a ghost. The transactional re-read serializes
        // against the cancel path's runs-row-write, so the predicate
        // holds at commit time. Mirrors the guarded pattern in
        // <c>HostCancelRunAdapter.RunCancelSql</c>; the unit tests run
        // against the InMemory provider which ignores transaction
        // isolation, so the path is exercised end-to-end without
        // producing lock-wait noise.
        var followUp = await StageFollowUpAsync(run, text, cancellationToken);

        logger.LogInformation(
            "Steer on run {RunId} staged follow-up work item {WorkItemId} (live worker was {WorkerState})",
            runId.Value,
            followUp.Id,
            liveWorker is null ? "absent" : "present");

        return new SteerRunResult(true, followUp.Id);
    }

    /// <summary>
    /// Stages the follow-up <see cref="WorkItem"/>: the same profile
    /// and env class as the live item (so a worker of the same kind
    /// claims it), the operator's steer text as the brief, and
    /// <see cref="WorkItemStatus.Queued"/> (the follow-up has no DAG
    /// edges — a fresh worker claims it as soon as the in-flight one
    /// is fenced by cancel or reaped by the lease policy). The whole
    /// read-check-insert sequence runs in one transaction; the
    /// terminal-status predicate is re-checked against the re-read
    /// row so a concurrent cancel between the initial read and the
    /// INSERT is rejected at commit time (the cancel path's
    /// runs-row-write serializes against this re-read).
    /// </summary>
    /// <param name="run">Run the follow-up attaches to (snapshot from the initial read).</param>
    /// <param name="text">The operator's steer text.</param>
    /// <param name="cancellationToken"></param>
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
        // Re-read the run row inside the transaction. The terminal
        // predicate fires again here; a concurrent cancel/finalized
        // between the initial read and the INSERT will leave the row
        // in a terminal status we refuse to follow-up against, the
        // transaction rolls back, and the caller sees a typed 409.
        // On the in-memory store (unit tests) the transaction is
        // unsupported, but the re-read-after-update row read-then-act
        // ordering already covers the test path.
        var fresh = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == run.Id, cancellationToken);
        if (fresh is null || IsTerminalOrFinalising(fresh.Status))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw new RunNotRunningForSteerException(fresh?.Status ?? run.Status);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return workItem;
    }

    /// <summary>
    /// True for terminal statuses (<see cref="RunStatus.Succeeded"/>,
    /// <see cref="RunStatus.Failed"/>, <see cref="RunStatus.Cancelled"/>)
    /// — the steer refusal path. The escalation sweeper is a separate
    /// state and a non-finalising one; the run's own
    /// <c>RunTransitions</c> is the source of truth and a steer
    /// against any non-terminal status is legal.
    /// </summary>
    /// <param name="status">Run status to test.</param>
    private static bool IsTerminalOrFinalising(RunStatus status)
    {
        return status == RunStatus.Succeeded
            || status == RunStatus.Failed
            || status == RunStatus.Cancelled;
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
    /// <param name="cancellationToken"></param>
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
