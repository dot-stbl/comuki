namespace Comuki.Modules.Work.Domain.Decisions;

/// <summary>
/// Who raised a <see cref="Decision"/> — the umbrella's task 5.6 invariant
/// ("no LLM direct transition") plus the <c>add-mission-cowork/architecture.md</c>
/// reviewer-separation rule. A Brain proposal needs a distinct-human approval
/// to be effective; a Deterministic decision runs as-is.
/// </summary>
public enum DecisionProvenance
{
    /// <summary>A policy-driven deterministic handler — no LLM, no human, no approval needed.</summary>
    Deterministic = 1,

    /// <summary>Brain auto-assigned under autonomy/policy; no human approval required to apply.</summary>
    BrainAssisted = 2,

    /// <summary>Human raised the decision — recorded for audit; reviewer separation enforces a distinct-human approval per <see cref="Completion.WorkTaskCompletionPolicy"/>.</summary>
    HumanReviewer = 3,
}
