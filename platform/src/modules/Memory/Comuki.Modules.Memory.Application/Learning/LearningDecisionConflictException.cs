using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;

namespace Comuki.Modules.Memory.Application.Learning;

/// <summary>
/// A decision endpoint hit a candidate whose review state is the wrong
/// source — approving a rejected candidate, rejecting a decided one. Carries
/// the candidate's current status so the API can answer 409 with it.
/// </summary>
/// <param name="candidateId">The candidate the decision named.</param>
/// <param name="current">The candidate's current review state.</param>
public sealed class LearningDecisionConflictException(
    LearningCandidateId candidateId,
    LearningStatus current) : InvalidOperationException(
        $"learning candidate {candidateId.Value} is already {LearningStatusKeys.Key(current)}")
{
    /// <summary>The candidate the decision named.</summary>
    public LearningCandidateId CandidateId { get; } = candidateId;

    /// <summary>The candidate's current review state.</summary>
    public LearningStatus Current { get; } = current;
}
