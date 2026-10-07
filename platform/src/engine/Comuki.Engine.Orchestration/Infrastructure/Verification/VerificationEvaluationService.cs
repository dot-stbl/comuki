using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Verification;

/// <summary>
/// Settings for the verification evaluation path
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "ProjectSettings.VerifyEnabled wires the verification path"). The
/// only project-level switch the host binds; the
/// <c>Verify:Verifier:Enabled</c> flag on the existing Verify module
/// is a separate axis (the runtime that runs the provider's gate
/// command), not the gate-enable switch.
/// </summary>
public sealed class VerificationOptions
{
    /// <summary>Configuration section the host binds from.</summary>
    public const string SectionName = "Orchestration:Verification";

    /// <summary>
    /// When <see langword="false"/> the orchestrator never calls a
    /// provider and never writes a <c>VerificationRecord</c> —
    /// <c>ProjectSettings.VerifyEnabled</c> is bypassed and the run
    /// lands as <c>Succeeded</c> with the existing semantics. The
    /// default mirrors the project rule that the field carries zero
    /// consumers today; the platform keeps the gate off until an
    /// operator opts the project in.
    /// </summary>
    public bool Enabled { get; init; }
}

/// <summary>
/// Per-work-item verification evaluator
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirements
/// "VerificationRecord is a per-WorkItem sibling table" + "gate_evaluated
/// journal event"). The host calls <see cref="EvaluateAsync"/> when
/// a work item enters a terminal phase; the service iterates the
/// registered providers, upserts the verdict into the
/// <c>verifications</c> table, and stamps the <c>gate.evaluated</c>
/// event in the same transaction. <c>ProjectSettings.VerifyEnabled</c>
/// is the per-project skip — the field is read on every evaluation
/// so a project-toggle takes effect for the next work item without a
/// restart.
/// </summary>
/// <param name="store">Per-scope record store.</param>
/// <param name="registry">Singleton provider registry.</param>
/// <param name="db">Orchestration context of the current scope — the journal append shares its transaction with the record upsert.</param>
/// <param name="journal">Run-journal writer for the <c>gate.evaluated</c> event.</param>
/// <param name="projectSettings">Per-project settings port (the engine never references the Projects module — the host composes the adapter).</param>
/// <param name="options">Verification options (host-level kill switch).</param>
/// <param name="clock">Wall-clock for the <c>evaluated_at</c> stamp.</param>
/// <param name="logger">Structured logger.</param>
public sealed class VerificationEvaluationService(
    IVerificationRecordStore store,
    IVerificationProviderRegistry registry,
    OrchestrationDbContext db,
    IRunJournal journal,
    IProjectVerificationSettings projectSettings,
    IOptions<VerificationOptions> options,
    TimeProvider clock,
    ILogger<VerificationEvaluationService> logger)
{
    /// <summary>
    /// Evaluates every registered provider for one work item. The
    /// journal event and the record upsert land in the caller's
    /// ambient transaction (the worker terminalization path
    /// already has one open — adding a new scope here would race
    /// the work-item state update).
    /// </summary>
    /// <param name="workItemId">The work item the gate set evaluates.</param>
    /// <param name="runId">The run the work item belongs to (joins the journal entry).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public async Task EvaluateAsync(
        Guid workItemId,
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        // The work item is the join key: its row carries the run id
        // (we already have it from the caller) and, via the run, the
        // project id. The query uses AsNoTracking + the primary key
        // so it never loads the work item into the change tracker —
        // the terminalization path already has a tracked instance.
        if (await db.WorkItems
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == workItemId, cancellationToken) is not { } workItem)
        {
            // The work item row was deleted between terminalization
            // and the verification call (e.g. by a concurrent reaper
            // race). Treat the gate as "no verdict" — the journal
            // does not get a half-stamped event.
            return;
        }

        // The run carries the project id directly — no extra join.
        // ProjectSettings.VerifyEnabled is the per-project switch; a
        // null project (cross-project global runs) short-circuits to
        // false. The host-level adapter reads the cached snapshot the
        // settings refresher keeps warm, so the call is allocation-free.
        if (await db.Runs
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) is not { } run)
        {
            return;
        }

        var context = new VerificationContext(
            WorkItemId: workItemId,
            RunId: runId,
            ProjectId: run.ProjectId,
            ProjectVerifyEnabled: projectSettings.IsVerificationEnabled(run.ProjectId));

        var now = clock.GetUtcNow();

        foreach (var provider in registry.Snapshot())
        {
            if (!provider.AppliesTo(context))
            {
                // The contract: AppliesTo returning false short-circuits
                // the evaluation and the host treats the gate as Pending
                // (no record written, no event stamped). The cleanest
                // shape here is a "did-not-apply" sentinel — the
                // spec scopes record writes to evaluated verdicts.
                logger.LogDebug(
                    "Gate {GateName} does not apply to work item {WorkItemId}; skipping",
                    provider.GateName,
                    workItemId);
                continue;
            }

            // Producer hook (add-orchestra §3 — Coda, task 3.2).
            // Gates that need to schedule an external artefact
            // (GenericCommandGateProvider inserts a GenericCommandRun
            // here) override EnsureGateRunAsync; the default no-op
            // keeps pure read-side providers unchanged. The producer
            // runs BEFORE the verdict read so the work item the
            // verifier worker polls has a row to claim on the very
            // first evaluation pass. The hook is idempotent on the
            // underlying partial index — a re-evaluation for the same
            // (work item, gate) is a no-op.
            try
            {
                await provider.EnsureGateRunAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The producer is best-effort: a misbehaving scheduler
                // must not break the verdict read. The catch mirrors
                // the EvaluateAsync catch — same exception, same
                // verdict semantics. Pending is the spec's canonical
                // "the provider could not answer" wire value; the
                // missing row on the verifier side surfaces as
                // Pending on the next evaluation pass, never as a
                // crash.
                logger.LogWarning(exception,
                    "Gate {GateName} producer threw on work item {WorkItemId}; continuing",
                    provider.GateName,
                    workItemId);
            }

            GateVerdictResult result;
            try
            {
                result = await provider.EvaluateAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Provider must be idempotent and must not throw on
                // transient upstream failures — the SPI contract says
                // "return Pending with empty evidence and log a
                // warning". The catch here is the platform's safety
                // net for a misbehaving provider; the verdict it
                // stamps is Pending so the journal + row reflect the
                // "the provider could not answer" state, not a crash.
                logger.LogWarning(exception,
                    "Gate {GateName} threw on work item {WorkItemId}; stamping Pending",
                    provider.GateName,
                    workItemId);
                result = new GateVerdictResult(GateVerdict.Pending, [], provider.GateName);
            }

            var record = VerificationRecord.FromEvaluation(
                workItemId: workItemId,
                gateName: provider.GateName,
                result: result,
                now: now);

            await store.UpsertAsync(record, cancellationToken);

            // Stamped in the same scope as the upsert — both share the
            // caller's transaction. AppendAsync on the journal port
            // is the DbContext's journal writer (the WorkItemQueueEf
            // terminal path uses the same scope).
            var evidenceRefs = result.Evidence
                .Select(evidence => evidence.Uri.ToString())
                .ToArray();

            var entry = new RunEventEntry(
                Id: Guid.NewGuid(),
                RunId: runId,
                Type: RunEventTypes.GateEvaluated,
                PayloadJson: WorkItemEventPayloads.GateEvaluated(
                    workItemId: workItemId,
                    recordId: record.Id,
                    gateName: provider.GateName,
                    verdict: result.Verdict.Value,
                    evidenceRefs: evidenceRefs,
                    evaluator: result.Evaluator),
                OccurredAt: now);

            await journal.AppendAsync(entry, cancellationToken);

            logger.LogInformation(
                "Gate {GateName} evaluated work item {WorkItemId}: {Verdict} ({EvidenceCount} evidence ref(s))",
                provider.GateName,
                workItemId,
                result.Verdict.Value,
                evidenceRefs.Length);
        }
    }
}
