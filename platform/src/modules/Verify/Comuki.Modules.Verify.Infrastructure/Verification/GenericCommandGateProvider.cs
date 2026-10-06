using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Contracts.Verification;
using Microsoft.Extensions.Logging;

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
/// <param name="logger">Structured logger.</param>
public sealed class GenericCommandGateProvider(
    IGenericCommandStore store,
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
        return run.Status.Value switch
        {
            // Green → Passed. The verifier worker is the authority on
            // the verdict; the gate is the witness that copies it
            // onto the verification axis.
            nameof(GenericCommandStatus.Green) => new GateVerdictResult(
                GateVerdict.Passed, [], GateName),
            // Red → Failed. The worker stamped Red either on a
            // non-matching exit code or on a launch failure.
            nameof(GenericCommandStatus.Red) => new GateVerdictResult(
                GateVerdict.Failed, [], GateName),
            // Pending / Running — the gate is in flight.
            _ => new GateVerdictResult(GateVerdict.Pending, [], GateName),
        };
    }
}
