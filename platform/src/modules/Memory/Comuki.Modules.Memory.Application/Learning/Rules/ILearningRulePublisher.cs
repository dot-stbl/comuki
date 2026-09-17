using Comuki.Modules.Memory.Application.Views;

namespace Comuki.Modules.Memory.Application.Learning.Rules;

/// <summary>
/// Where an approved learning candidate's rule goes — the strategy seam of
/// the learning loop's approve step. v1 ships the memory-fact publisher
/// (<see cref="MemoryLearningRulePublisher"/>: a standing project fact with
/// source <c>learning-approved</c>, discoverable via memory.recall); v2 adds a
/// PR-into-git publisher over the client's rules repository without the
/// service or the controller changing shape.
/// </summary>
public interface ILearningRulePublisher
{
    /// <summary>
    /// Publishes the approved candidate's rule. Idempotent per candidate —
    /// re-publishing (the retry after a crash between approve and publish)
    /// supersedes the previous rule fact rather than duplicating it.
    /// </summary>
    /// <param name="candidate">The approved candidate whose rule is published.</param>
    /// <param name="cancellationToken"></param>
    public Task PublishAsync(LearningCandidateView candidate, CancellationToken cancellationToken = default);
}
