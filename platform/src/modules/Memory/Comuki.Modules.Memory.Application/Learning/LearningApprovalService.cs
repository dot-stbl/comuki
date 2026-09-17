using Comuki.Modules.Memory.Application.Learning.Rules;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Application.Learning;

/// <summary>
/// The approve/reject coordination of the learning loop. Approve is
/// decided-then-published with an idempotent tail: the store flips the
/// candidate (or leaves an already-approved one alone), then the publisher
/// makes the rule visible — so the retry after a crash between the two
/// converges instead of dead-ending in a 409 on an unpublished rule.
/// </summary>
/// <param name="candidates"></param>
/// <param name="rulePublisher"></param>
/// <param name="clock"></param>
public sealed class LearningApprovalService(
    ILearningCandidateStore candidates,
    ILearningRulePublisher rulePublisher,
    TimeProvider clock)
{
    /// <summary>
    /// Marks the candidate approved and publishes its rule. Throws
    /// <see cref="LearningDecisionConflictException"/> when the candidate is
    /// rejected; returns null when it does not exist.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public async Task<LearningCandidateView?> ApproveAsync(LearningCandidateId id, CancellationToken cancellationToken = default)
    {
        var decided = await candidates.ApproveAsync(id, clock.GetUtcNow(), cancellationToken);
        if (decided is null)
        {
            return null;
        }

        await rulePublisher.PublishAsync(decided, cancellationToken);
        return decided;
    }

    /// <summary>
    /// Marks the candidate rejected with an optional human reason; a rejected
    /// rule is never published. Throws
    /// <see cref="LearningDecisionConflictException"/> when already decided;
    /// returns null when the candidate does not exist.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="reason"></param>
    /// <param name="cancellationToken"></param>
    public Task<LearningCandidateView?> RejectAsync(LearningCandidateId id, string? reason = null, CancellationToken cancellationToken = default)
    {
        return candidates.RejectAsync(id, clock.GetUtcNow(), reason, cancellationToken);
    }
}
