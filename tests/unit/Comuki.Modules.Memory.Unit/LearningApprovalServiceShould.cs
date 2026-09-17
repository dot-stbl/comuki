using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Learning.Rules;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The learning loop's decision coordination: approve decides then
/// publishes (with the idempotent already-approved retry), reject decides
/// and never publishes.
/// </summary>
public sealed class LearningApprovalServiceShould
{
    private static readonly DateTimeOffset decidedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a pending candidate, when approved, then the store decides it and the rule is published")]
    public async Task ApproveDecidesThenPublishesAsync()
    {
        var candidate = NewCandidate(LearningStatus.Pending);
        var store = Substitute.For<ILearningCandidateStore>();
        var publisher = Substitute.For<ILearningRulePublisher>();
        var service = NewService(store, publisher);

        store.ApproveAsync(Arg.Any<LearningCandidateId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(candidate with { Status = LearningStatusKeys.Key(LearningStatus.Approved) });

        var decided = await service.ApproveAsync(Id(candidate), TestContext.Current.CancellationToken);

        decided.ShouldNotBeNull();
        decided.Status.ShouldBe(LearningStatusKeys.Key(LearningStatus.Approved));
        await store.Received(1).ApproveAsync(Id(candidate), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(
            Arg.Is<LearningCandidateView>(view => view.Id == candidate.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an absent candidate, when approved, then null flows back and nothing is published")]
    public async Task ApproveAbsentCandidatePublishesNothingAsync()
    {
        var store = Substitute.For<ILearningCandidateStore>();
        var publisher = Substitute.For<ILearningRulePublisher>();
        var service = NewService(store, publisher);
        var unknown = LearningCandidateId.New();

        store.ApproveAsync(Arg.Any<LearningCandidateId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((LearningCandidateView?)null);

        var decided = await service.ApproveAsync(unknown, TestContext.Current.CancellationToken);

        decided.ShouldBeNull();
        await publisher.DidNotReceive().PublishAsync(Arg.Any<LearningCandidateView>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a pending candidate, when rejected with a reason, then the store records the rejection and the rule is never published")]
    public async Task RejectRecordsReasonWithoutPublishingAsync()
    {
        var candidate = NewCandidate(LearningStatus.Pending);
        var store = Substitute.For<ILearningCandidateStore>();
        var publisher = Substitute.For<ILearningRulePublisher>();
        var service = NewService(store, publisher);

        store.RejectAsync(Arg.Any<LearningCandidateId>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidate);

        var decided = await service.RejectAsync(Id(candidate), "already covered", TestContext.Current.CancellationToken);

        decided.ShouldNotBeNull();
        await store.Received(1).RejectAsync(
            Id(candidate), Arg.Any<DateTimeOffset>(), "already covered", Arg.Any<CancellationToken>());
        await publisher.DidNotReceive().PublishAsync(Arg.Any<LearningCandidateView>(), Arg.Any<CancellationToken>());
    }

    private static LearningApprovalService NewService(
        ILearningCandidateStore store,
        ILearningRulePublisher publisher)
    {
        return new LearningApprovalService(store, publisher, new FixedDecidedClock());
    }

    private static LearningCandidateId Id(LearningCandidateView candidate)
    {
        return new LearningCandidateId(candidate.Id);
    }

    private static LearningCandidateView NewCandidate(LearningStatus status)
    {
        return new LearningCandidateView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "build.dotnet",
            "three runs failed on the same cold-cache test",
            "run the warmup script before dotnet test",
            "worker:1",
            RepeatCount: 1,
            LearningStatusKeys.Key(status),
            DecisionReason: null,
            decidedAt.AddHours(-1),
            status == LearningStatus.Pending ? null : decidedAt);
    }

    private sealed class FixedDecidedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return decidedAt;
        }
    }
}
