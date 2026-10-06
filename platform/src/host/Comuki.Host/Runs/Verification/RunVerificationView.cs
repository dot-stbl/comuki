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
/// Run-level wire shape for <c>GET /api/v1/runs/{runId}/verification</c>
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "Verification view is a derived read"). The two top-level booleans
/// surface the "verification pending" annotation the FE renders as a
/// pill under the run's status strip; the per-gate list renders as the
/// evidence drill-down beneath it. The semantics are derived from the
/// <see cref="Gates"/> list alone — a future per-project
/// <c>VerifyEnabled</c> read is gated on the registry returning at
/// least one record (the project is provably opted in if a provider
/// has stamped anything).
/// </summary>
/// <param name="RunId">Run the verdicts belong to.</param>
/// <param name="Verified">
/// <see langword="true"/> when every gate in <see cref="Gates"/> is
/// <c>passed</c> AND at least one gate has been evaluated — the run's
/// verification axis is fully terminal and positive. A run with no
/// gates yet, with mixed <c>passed</c>/<c>pending</c>, or with any
/// <c>failed</c> is <see langword="false"/>.</param>
/// <param name="VerificationPending">
/// <see langword="true"/> when at least one gate is still <c>pending</c>
/// AND the run is not yet fully verified — the "verification pending"
/// annotation the spec introduces. Terminal failures do NOT count as
/// pending: a run with one <c>pending</c> and one <c>failed</c> gate
/// is <c>pending=true</c>; a run with only <c>failed</c> is
/// <c>pending=false</c>.</param>
/// <param name="Gates">One entry per evaluated (work item, gate) pair. Newest first within a work item.</param>
public sealed record RunVerificationView(
    Guid RunId,
    bool Verified,
    bool VerificationPending,
    IReadOnlyList<VerificationGateView> Gates);
