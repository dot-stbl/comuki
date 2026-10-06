using Comuki.Shared.Contracts.Verification;

namespace Comuki.Engine.Orchestration.Domain.Verification;

/// <summary>
/// Per-work-item verification record (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "VerificationRecord is a
/// per-WorkItem sibling table"). The row lives in the orchestration
/// schema on the <c>verifications</c> table — indexed on
/// <c>(work_item_id, gate_name)</c> so a gate evaluates at most once
/// per (work item, gate) pair (the unique-index upsert is the
/// re-evaluation contract — a second evaluation produces an updated
/// row, never a duplicate). The seven run states and the run
/// transition table are unaffected; this aggregate is a sibling
/// surface, not a column on <c>work_items</c> and not a run state.
/// </summary>
public sealed class VerificationRecord
{
    private VerificationRecord() { }

    /// <summary>Strong-typed record id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The work item the gate verdicts. Raw <c>Guid</c> to match
    /// the engine's <c>WorkItem</c> shape (the platform strong-types
    /// <c>RunId</c> / <c>ProjectId</c> but not <c>WorkItemId</c>).
    /// </summary>
    public Guid WorkItemId { get; private set; }

    /// <summary>Stable gate name (matches <see cref="IVerificationGateProvider.GateName"/>).</summary>
    public string GateName { get; private set; } = string.Empty;

    /// <summary>The current verdict the gate evaluator produced.</summary>
    public GateVerdict Verdict { get; private set; }

    /// <summary>
    /// Evidence the gate attached to the verdict. Stored as
    /// <c>jsonb</c> (see <c>VerificationRecordConfiguration</c>).
    /// Empty when the gate returned no pointers.
    /// </summary>
    public IReadOnlyList<GateEvidenceRef> EvidenceRefs { get; private set; } = [];

    /// <summary>When the (re)evaluation finished. Mutates on every upsert.</summary>
    public DateTimeOffset EvaluatedAt { get; private set; }

    /// <summary>Provider name stamped at the moment of the verdict (matches the gate name; kept as a self-describing field).</summary>
    public string Evaluator { get; private set; } = string.Empty;

    /// <summary>
    /// Creates a fresh <c>VerificationRecord</c> in <c>Pending</c> —
    /// the only state the host can stamp without a real evaluator
    /// running (the host never does this today, but the contract is
    /// here for a future "register a gate that's still in flight"
    /// flow). A real evaluation always uses <see cref="FromEvaluation"/>
    /// to construct the verdict-bearing row.
    /// </summary>
    /// <param name="workItemId">Work item the row belongs to.</param>
    /// <param name="gateName">Stable gate name (non-empty).</param>
    /// <param name="now">Wall-clock stamp.</param>
    public static VerificationRecord Create(
        Guid workItemId,
        string gateName,
        DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(gateName)
            ? throw new ArgumentException("gate name must not be empty", nameof(gateName))
            : new VerificationRecord
            {
                Id = Guid.CreateVersion7(),
                WorkItemId = workItemId,
                GateName = gateName.Trim(),
                Verdict = GateVerdict.Pending,
                EvidenceRefs = [],
                EvaluatedAt = now,
                Evaluator = gateName.Trim(),
            };
    }

    /// <summary>
    /// Stamps a real evaluation on a row (or a new row). The
    /// reconstitute path is for the EF store's materialiser; the
    /// factory is the only entry callers should use — the constructor
    /// stays <c>private</c> for the same reason every other aggregate
    /// in the engine does.
    /// </summary>
    /// <param name="workItemId">Work item the row belongs to.</param>
    /// <param name="gateName">Stable gate name.</param>
    /// <param name="result">The gate's verdict + evidence + evaluator.</param>
    /// <param name="now">Wall-clock stamp for <see cref="EvaluatedAt"/>.</param>
    public static VerificationRecord FromEvaluation(
        Guid workItemId,
        string gateName,
        GateVerdictResult result,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(gateName))
        {
            throw new ArgumentException("gate name must not be empty", nameof(gateName));
        }

        // `result` is a non-nullable record; the compiler enforces the
        // nullability. The ArgumentNullException.ThrowIfNull call the
        // project rule bans (code-shape.md §11) is duplicate noise.

        return new VerificationRecord
        {
            Id = Guid.CreateVersion7(),
            WorkItemId = workItemId,
            GateName = gateName.Trim(),
            Verdict = result.Verdict,
            EvidenceRefs = result.Evidence ?? [],
            EvaluatedAt = now,
            Evaluator = string.IsNullOrWhiteSpace(result.Evaluator)
                ? gateName.Trim()
                : result.Evaluator.Trim(),
        };
    }

    /// <summary>
    /// EF Core's materialiser calls this with a row read back from
    /// <c>verifications</c>. The constructor stays <c>private</c> —
    /// production code uses <see cref="Create"/> /
    /// <see cref="FromEvaluation"/> so the invariants on
    /// <see cref="GateName"/> stay compile-checked at the call site.
    /// </summary>
    internal static VerificationRecord Reconstitute(
        Guid id,
        Guid workItemId,
        string gateName,
        GateVerdict verdict,
        IReadOnlyList<GateEvidenceRef> evidenceRefs,
        DateTimeOffset evaluatedAt,
        string evaluator)
    {
        return new VerificationRecord
        {
            Id = id,
            WorkItemId = workItemId,
            GateName = gateName,
            Verdict = verdict,
            EvidenceRefs = evidenceRefs,
            EvaluatedAt = evaluatedAt,
            Evaluator = evaluator,
        };
    }
}
