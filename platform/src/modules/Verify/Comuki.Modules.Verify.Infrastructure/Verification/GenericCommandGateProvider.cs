using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Contracts.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Verify.Infrastructure.Verification;

/// <summary>
/// Platform-shipped first gate provider
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "First registered provider is the existing Verify module"). The
/// provider reads the most recent <c>GenericCommandRun</c> for the
/// bound work item and stamps the verdict from its terminal status:
/// <c>Green</c> → <c>Passed</c>, <c>Red</c> → <c>Failed</c>, no row
/// (operator-only or unstarted) → <c>Pending</c>. The
/// <c>GenericCommandVerifierWorker</c> has already advanced the row
/// through <c>Pending → Running → Green/Red</c>; this provider is the
/// read-side witness that stamps the verdict on the work item.
/// </summary>
/// <param name="store">Generic-command store, scoped per call.</param>
/// <param name="clock">Wall-clock for the <see cref="GenericCommandRun.CreatedAt"/> stamp.</param>
/// <param name="commandGateOptions">Bound command-gate configuration — empty <see cref="CommandGateOptions.Command"/> keeps the producer off.</param>
/// <param name="logger">Structured logger.</param>
public sealed class GenericCommandGateProvider(
    IGenericCommandStore store,
    TimeProvider clock,
    IOptions<CommandGateOptions> commandGateOptions,
    ILogger<GenericCommandGateProvider> logger) : IVerificationGateProvider
{
    /// <summary>
    /// Stable dotted gate name — the row key on
    /// <c>VerificationRecord</c>, the value stamped on
    /// <see cref="GateVerdictResult.Evaluator"/>, and the literal the
    /// registry's <c>AppliesTo</c> callers branch on.
    /// </summary>
    public string GateName => "verify:generic-command-run";

    /// <inheritdoc />
    public bool AppliesTo(VerificationContext context)
    {
        // The provider applies to a work item when a generic-command
        // run exists for it. The lookup is the same shape the
        // verifier worker uses for the claim query; the index
        // (project_id, work_item_id) added in this change covers it.
        // A null WorkItemId or a project that disables verification
        // short-circuits the gate to Pending — the spec says no
        // record, no event in that case.
        if (context.ProjectId is null)
        {
            return false;
        }

        if (!context.ProjectVerifyEnabled)
        {
            return false;
        }

        // The actual existence check is in EvaluateAsync — AppliesTo
        // is a quick filter, the real verdict read is in the eval.
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Producer step (add-orchestra §3 — Coda, task 3.2): when the
    /// command-gate section is bound, insert a
    /// <see cref="GenericCommandRun"/> for the work item on the first
    /// evaluation pass and hand it to the existing verify worker
    /// (the worker's claim query uses <c>FOR UPDATE SKIP LOCKED</c>;
    /// the partial index <c>ix_generic_command_runs_project_work_item</c>
    /// covers the existence check). The step is idempotent — a
    /// re-evaluation for the same (work item, gate) pair sees the
    /// existing run and does not insert a duplicate. When the section
    /// is unbound the producer no-ops and the gate stays Pending
    /// until the operator schedules a run by hand.
    /// </remarks>
    public async Task EnsureGateRunAsync(
        VerificationContext context,
        CancellationToken cancellationToken = default)
    {
        var options = commandGateOptions.Value;

        // Empty command = operator hasn't opted the project in. The
        // gate stamps Pending on the next read; no row is scheduled.
        if (string.IsNullOrWhiteSpace(options.Command))
        {
            return;
        }

        if (context.ProjectId is null)
        {
            // A null project means the run is cross-project / global —
            // the producer only schedules work-item-bound runs. The
            // existing AppliesTo short-circuits the same condition,
            // so this branch is unreachable today; the guard keeps
            // the producer safe if a future caller drops the check.
            return;
        }

        // Existence-first partial index lookup: ListByWorkItemAsync
        // orders newest first; a single match is enough to skip the
        // insert and stay idempotent.
        var existing = await store.ListByWorkItemAsync(
            context.ProjectId.Value,
            context.WorkItemId,
            limit: 1,
            cancellationToken);

        if (existing.Count > 0)
        {
            // The (project_id, work_item_id) pair already has a run;
            // the existing verifier worker picks it up on its next
            // poll, and the gate stamps the corresponding record. The
            // producer is a no-op on every subsequent evaluation.
            return;
        }

        var run = GenericCommandRun.Create(
            projectId: context.ProjectId.Value,
            profileKey: options.ProfileKey,
            executable: options.Command,
            arguments: options.Arguments,
            expectedExitCode: options.ExpectedExitCode,
            now: clock.GetUtcNow(),
            workItemId: context.WorkItemId);

        await store.AddAsync(run, cancellationToken);

        logger.LogInformation(
            "GenericCommandGateProvider: scheduled run {RunId} for work item {WorkItemId} (command '{Command}')",
            run.Id.Value,
            context.WorkItemId,
            options.Command);
    }

    /// <inheritdoc />
    public async Task<GateVerdictResult> EvaluateAsync(
        VerificationContext context,
        CancellationToken cancellationToken = default)
    {
        // `context` is a non-nullable record; the compiler enforces
        // the nullability. The ArgumentNullException.ThrowIfNull call
        // the project rule bans (code-shape.md §11) is duplicate noise.

        // AppliesTo already filtered the project scope; here we just
        // read the most recent run for the work item. The store's
        // ListAsync orders newest first.
        var runs = await store.ListByWorkItemAsync(
            context.ProjectId!.Value,
            context.WorkItemId,
            limit: 1,
            cancellationToken);

        if (runs.Count == 0)
        {
            // No run has been scheduled for this work item yet — the
            // gate is in flight. The platform's "Pending" wire is the
            // spec's canonical "no verdict yet" value.
            logger.LogDebug(
                "GenericCommandGateProvider: no run for work item {WorkItemId}; stamping Pending",
                context.WorkItemId);
            return new GateVerdictResult(GateVerdict.Pending, [], GateName);
        }

        var run = runs[0];
        return run.Status == GenericCommandStatus.Green
            ? new GateVerdictResult(GateVerdict.Passed, [], GateName)
            : run.Status == GenericCommandStatus.Red
                ? new GateVerdictResult(GateVerdict.Failed, [], GateName)
                : new GateVerdictResult(GateVerdict.Pending, [], GateName);
    }
}
