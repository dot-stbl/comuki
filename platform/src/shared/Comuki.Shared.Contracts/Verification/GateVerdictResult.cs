namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Verdict a single gate evaluation produced. The host writes one of
/// these to the journal as a <c>gate.evaluated</c> event in the same
/// transaction as the <c>VerificationRecord</c> upsert (add-orchestra
/// §3 — Coda, <c>verification/spec.md</c> Requirement "gate_evaluated
/// journal event"). <see cref="Evidence"/> is the stable URI list a
/// gate attached to its verdict; <see cref="Evaluator"/> is the stable
/// string the provider stamps as its name (matches
/// <see cref="IVerificationGateProvider.GateName"/> — kept here as a
/// self-describing field so the row stands alone without a join).
/// </summary>
/// <param name="Verdict">The gate's verdict for the work item.</param>
/// <param name="Evidence">Canonical evidence URIs the gate attached. May be empty.</param>
/// <param name="Evaluator">Stable provider name (matches <see cref="IVerificationGateProvider.GateName"/>) — recorded on the row for provenance.</param>
public sealed record GateVerdictResult(
    GateVerdict Verdict,
    IReadOnlyList<GateEvidenceRef> Evidence,
    string Evaluator);
