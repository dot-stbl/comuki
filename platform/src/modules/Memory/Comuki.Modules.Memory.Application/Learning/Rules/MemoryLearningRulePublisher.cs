using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;

namespace Comuki.Modules.Memory.Application.Learning.Rules;

/// <summary>
/// Option A rule publisher: an approved candidate becomes a standing memory
/// fact of its project — source <see cref="MemorySource.LearningApproved"/>,
/// topic <c>rule.{topic}</c>, the proposed rule as the text — so the brain
/// and future workers find it through memory.recall like any other
/// project constraint. The observation rides along in the text: a rule
/// without its evidence cannot be judged by the next reader.
/// </summary>
/// <param name="memoryStore"></param>
public sealed class MemoryLearningRulePublisher(IMemoryStore memoryStore) : ILearningRulePublisher
{
    /// <summary>The topic-key prefix that marks a fact as an approved learning rule.</summary>
    public const string RuleTopicPrefix = "rule.";

    /// <inheritdoc />
    public async Task PublishAsync(LearningCandidateView candidate, CancellationToken cancellationToken = default)
    {
        var text = candidate.Observation.Length == 0
            ? candidate.ProposedRule
            : $"{candidate.ProposedRule} (observed: {candidate.Observation})";

        await memoryStore.WriteAsync(
            new MemoryFactWrite(
                Scope: MemoryScope.Project,
                SubjectId: candidate.ProjectId.ToString(),
                Kind: MemoryFactKind.Standing,
                TopicKey: $"{RuleTopicPrefix}{candidate.Topic}",
                Text: text,
                Source: MemorySource.LearningApproved,
                CreatedBy: $"learning:{candidate.Id}"),
            cancellationToken);
    }
}
