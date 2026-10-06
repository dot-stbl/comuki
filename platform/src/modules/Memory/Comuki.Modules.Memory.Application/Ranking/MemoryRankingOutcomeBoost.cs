using Comuki.Modules.Memory.Domain.Learning;

namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// Interim outcome-signal boost that the memory fallback ranking uses
/// to order facts when embeddings and FTS are both unavailable (the
/// add-chat-memory contract's hard floor). The boost is the sum of
/// per-candidate task-success deltas (succeeded minus failed), capped
/// at the default <see cref="CappedSignedContribution.DefaultCap"/>
/// through the project's <see cref="CappedSignedContribution.Apply"/>
/// helper — the same shape the learning loop uses to maintain a
/// candidate's outcome total.
///
/// The wiring is the interim consumer of <see cref="CappedSignedContribution"/>:
/// a real source (a per-project repository of
/// <see cref="LearningCandidate"/> rows) replaces the placeholder
/// caller of <see cref="ForCandidates"/>.
/// </summary>
public static class MemoryRankingOutcomeBoost
{
    /// <summary>
    /// Computes the boost for one scope from a snapshot of
    /// <see cref="LearningCandidate"/> rows. Each candidate contributes
    /// its (succeeded - failed) delta to the running total; the final
    /// total is clamped to <c>[-cap, +cap]</c> via
    /// <see cref="CappedSignedContribution.Apply"/>.
    /// </summary>
    /// <param name="candidates">
    /// The project's learning candidates, in any order. Empty means
    /// no signal — the returned boost is zero.
    /// </param>
    /// <param name="cap">
    /// Symmetric clamp. <c>null</c> falls back to
    /// <see cref="CappedSignedContribution.DefaultCap"/>.
    /// </param>
    public static int ForCandidates(IReadOnlyList<LearningCandidate> candidates, int? cap = null)
    {
        var delta = 0;
        foreach (var candidate in candidates)
        {
            delta += candidate.TaskSucceededCount - candidate.TaskFailedCount;
        }

        return CappedSignedContribution.Apply(current: 0, delta, cap);
    }
}
