using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;

namespace Comuki.Modules.Memory.Application.Learning;

/// <summary>
/// Persistence port over the learning-candidate queue: the write path of
/// <c>learning.suggest</c> (duplicate pending suggestions repeat-count
/// instead of queueing twice) and the read/decide paths of the approvals
/// surface. Decision methods apply the entity transitions and persist;
/// publication of an approved rule is <see cref="Rules.ILearningRulePublisher"/>'s
/// job, coordinated by <see cref="LearningApprovalService"/>.
/// </summary>
public interface ILearningCandidateStore
{
    /// <summary>
    /// Records one suggestion: a pending candidate with the same
    /// (project, topic, proposed rule) gets its repeat counter bumped;
    /// otherwise a fresh pending candidate is created.
    /// </summary>
    /// <param name="suggestion"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<LearningCandidateView> SuggestAsync(LearningSuggestion suggestion, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Lists candidates, newest first; null status = every state.</summary>
    /// <param name="status"></param>
    /// <param name="limit">Maximum rows returned.</param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<LearningCandidateView>> ListAsync(LearningStatus? status = null, int limit = DefaultListLimit, CancellationToken cancellationToken = default);

    /// <summary>Default page size for <see cref="ListAsync"/>.</summary>
    public const int DefaultListLimit = 200;

    /// <summary>Reads one candidate; null when absent.</summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public Task<LearningCandidateView?> GetAsync(LearningCandidateId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the candidate approved and persists the transition. Approving
    /// an already-approved candidate is a no-op (the retry after a publish
    /// failure re-enters here); a rejected one refuses with
    /// <see cref="LearningDecisionConflictException"/>. Null when absent.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<LearningCandidateView?> ApproveAsync(LearningCandidateId id, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the candidate rejected with an optional reason and persists
    /// the transition; deciding twice refuses with
    /// <see cref="LearningDecisionConflictException"/>. Null when absent.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="now"></param>
    /// <param name="reason"></param>
    /// <param name="cancellationToken"></param>
    public Task<LearningCandidateView?> RejectAsync(LearningCandidateId id, DateTimeOffset now, string? reason = null, CancellationToken cancellationToken = default);
}
