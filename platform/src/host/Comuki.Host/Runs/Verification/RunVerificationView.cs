namespace Comuki.Host.Runs.Verification;

/// <summary>
/// Per-gate wire shape for <c>GET /api/v1/runs/{runId}/verification</c>.
/// The verdict string is the lower-case <c>GateVerdict</c> smart-type
/// name (<c>pending</c> / <c>passed</c> / <c>failed</c>); the evidence URI
/// is returned as a plain string so the FE can resolve it through the
/// existing artifact endpoint without the engine re-encoding the URL.
/// </summary>
/// <param name="WorkItemId">Work item the gate evaluated.</param>
/// <param name="GateName">Provider-registered name (e.g. <c>verify:generic-command-run</c>).</param>
/// <param name="Verdict">Lower-case verdict name.</param>
/// <param name="EvidenceRefs">Evidence URIs the provider stamped; empty when the verdict is <c>Pending</c>.</param>
/// <param name="EvaluatedAt">UTC stamp the provider returned; matches the record's <c>evaluated_at</c>.</param>
/// <param name="Evaluator">Provider-defined evaluator string (operator name, model id, or <c>"system"</c>).</param>
public sealed record VerificationGateView(
    Guid WorkItemId,
    string GateName,
    string Verdict,
    IReadOnlyList<string> EvidenceRefs,
    DateTimeOffset EvaluatedAt,
    string Evaluator);

/// <summary>
/// Run-level wire shape for <c>GET /api/v1/runs/{runId}/verification</c>.
/// Groups per-gate verdicts by work item; the FE renders the list directly
/// under the run's work-item strip.
/// </summary>
/// <param name="RunId">Run the verdicts belong to.</param>
/// <param name="Gates">One entry per evaluated (work item, gate) pair. Newest first within a work item.</param>
public sealed record RunVerificationView(
    Guid RunId,
    IReadOnlyList<VerificationGateView> Gates);
