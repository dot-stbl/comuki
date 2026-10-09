namespace Comuki.Modules.Work.Domain.Completion;

/// <summary>
/// Closed set of <see cref="WorkTaskCompletionPolicy"/> kinds — who has the
/// final say on a Task's resolution outcome. The umbrella's task 8.1
/// canonical list.
/// </summary>
public enum CompletionPolicyKind
{
    /// <summary>A policy-driven deterministic handler decides — no LLM, no human review.</summary>
    Deterministic = 1,

    /// <summary>The Brain auto-decides under the autonomy/policy — no human approval needed.</summary>
    BrainAssisted = 2,

    /// <summary>A distinct-human reviewer must approve the proposed outcome (task 8.2 / 8.3 reviewer separation).</summary>
    HumanReviewer = 3,
}
