using Comuki.Modules.Memory.Application.Learning.Rules;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Learning;
using NSubstitute;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Option A rule publication: an approved candidate becomes a standing,
/// project-scoped memory fact with the learning-approved source and the
/// <c>rule.{topic}</c> topic key, its observation riding in the text.
/// </summary>
public sealed class MemoryLearningRulePublisherShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an approved candidate, when published, then a standing rule fact is written to the candidate's project")]
    public async Task PublishWritesStandingRuleFactAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        var publisher = new MemoryLearningRulePublisher(memoryStore);
        var candidate = NewCandidate();

        await publisher.PublishAsync(candidate, TestContext.Current.CancellationToken);

        await memoryStore.Received(1).WriteAsync(
            Arg.Is<MemoryFactWrite>(write =>
                write.Scope == MemoryScope.Project
                && write.SubjectId == candidate.ProjectId.ToString()
                && write.Kind == MemoryFactKind.Standing
                && write.TopicKey == $"rule.{candidate.Topic}"
                && write.Text.Contains(candidate.ProposedRule)
                && write.Text.Contains(candidate.Observation)
                && write.Source == MemorySource.LearningApproved
                && write.CreatedBy == $"learning:{candidate.Id}"),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a candidate with an empty observation, when published, then the fact text is the bare rule")]
    public async Task PublishWithoutObservationWritesBareRuleAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        var publisher = new MemoryLearningRulePublisher(memoryStore);
        var candidate = NewCandidate() with { Observation = string.Empty };

        await publisher.PublishAsync(candidate, TestContext.Current.CancellationToken);

        await memoryStore.Received(1).WriteAsync(
            Arg.Is<MemoryFactWrite>(write => write.Text == candidate.ProposedRule),
            Arg.Any<CancellationToken>());
    }

    private static LearningCandidateView NewCandidate()
    {
        return new LearningCandidateView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "build.dotnet",
            "three runs failed on the same cold-cache test",
            "run the warmup script before dotnet test",
            "worker:1",
            RepeatCount: 1,
            LearningStatusKeys.Key(LearningStatus.Approved),
            DecisionReason: null,
            now,
            now.AddMinutes(5));
    }
}
