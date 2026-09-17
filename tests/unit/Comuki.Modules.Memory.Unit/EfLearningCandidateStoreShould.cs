using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Queue semantics of <see cref="EfLearningCandidateStore"/> over the EF
/// Core in-memory provider: suggestion dedup (repeat counter), status
/// filtering, and the decision transitions with their conflict guard.
/// </summary>
public sealed class EfLearningCandidateStoreShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid projectId = Guid.NewGuid();

    [Fact(DisplayName = "Given a first suggestion, when suggested, then a pending candidate is queued")]
    public async Task SuggestQueuesPendingCandidateAsync()
    {
        using var session = NewSession();

        var queued = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);

        queued.Status.ShouldBe(LearningStatusKeys.Pending);
        queued.RepeatCount.ShouldBe(1);
        queued.ProjectId.ShouldBe(projectId);
        queued.Topic.ShouldBe("build.dotnet");
    }

    [Fact(DisplayName = "Given a pending candidate with the same project, topic and rule, when suggested again, then the repeat counter grows instead of queueing a duplicate")]
    public async Task SuggestRepeatsPendingDuplicateAsync()
    {
        using var session = NewSession();
        await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);

        var second = await session.Store.SuggestAsync(Suggestion(), now.AddMinutes(5), TestContext.Current.CancellationToken);
        var listed = await session.Store.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        second.RepeatCount.ShouldBe(2);
        listed.ShouldHaveSingleItem();
        listed[0].RepeatCount.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a decided candidate, when the same rule is suggested again, then a fresh pending review starts")]
    public async Task SuggestAfterDecisionStartsFreshReviewAsync()
    {
        using var session = NewSession();
        var first = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);
        await session.Store.RejectAsync(Id(first), now.AddHours(1), "covered elsewhere", TestContext.Current.CancellationToken);

        var second = await session.Store.SuggestAsync(Suggestion(), now.AddHours(2), TestContext.Current.CancellationToken);

        second.RepeatCount.ShouldBe(1);
        second.Status.ShouldBe(LearningStatusKeys.Pending);
    }

    [Fact(DisplayName = "Given candidates in mixed states, when listed with a status filter, then only that state returns")]
    public async Task ListFiltersByStatusAsync()
    {
        using var session = NewSession();
        var first = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);
        await session.Store.SuggestAsync(Suggestion("testing.xunit", "flaky test", "skip on cold cache"), now.AddMinutes(1), TestContext.Current.CancellationToken);
        await session.Store.ApproveAsync(Id(first), now.AddMinutes(2), TestContext.Current.CancellationToken);

        var pending = await session.Store.ListAsync(LearningStatus.Pending, cancellationToken: TestContext.Current.CancellationToken);
        var approved = await session.Store.ListAsync(LearningStatus.Approved, cancellationToken: TestContext.Current.CancellationToken);

        pending.ShouldHaveSingleItem();
        pending[0].Topic.ShouldBe("testing.xunit");
        approved.ShouldHaveSingleItem();
        approved[0].Topic.ShouldBe("build.dotnet");
    }

    [Fact(DisplayName = "Given an approved candidate, when approved again, then the retry is idempotent — no conflict, same row")]
    public async Task ApproveAlreadyApprovedIsIdempotentAsync()
    {
        using var session = NewSession();
        var queued = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);
        await session.Store.ApproveAsync(Id(queued), now.AddMinutes(1), TestContext.Current.CancellationToken);

        var retried = await session.Store.ApproveAsync(Id(queued), now.AddMinutes(2), TestContext.Current.CancellationToken);

        retried.ShouldNotBeNull();
        retried.Status.ShouldBe(LearningStatusKeys.Approved);
    }

    [Fact(DisplayName = "Given a rejected candidate, when approved, then the decision conflict carries the current status")]
    public async Task ApproveRejectedConflictsAsync()
    {
        using var session = NewSession();
        var queued = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);
        await session.Store.RejectAsync(Id(queued), now.AddMinutes(1), null, TestContext.Current.CancellationToken);

        var conflict = Should.Throw<LearningDecisionConflictException>(() =>
            session.Store.ApproveAsync(Id(queued), now.AddMinutes(2), TestContext.Current.CancellationToken));

        conflict.Current.ShouldBe(LearningStatus.Rejected);
    }

    [Fact(DisplayName = "Given a pending candidate, when rejected with a reason, then the reason lands on the row")]
    public async Task RejectKeepsReasonAsync()
    {
        using var session = NewSession();
        var queued = await session.Store.SuggestAsync(Suggestion(), now, TestContext.Current.CancellationToken);

        var rejected = await session.Store.RejectAsync(Id(queued), now.AddMinutes(1), "duplicate", TestContext.Current.CancellationToken);

        rejected.ShouldNotBeNull();
        rejected.Status.ShouldBe(LearningStatusKeys.Rejected);
        rejected.DecisionReason.ShouldBe("duplicate");
        rejected.DecidedAt.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given an unknown id, when decided or read, then null comes back without throwing")]
    public async Task UnknownIdAnswersNullAsync()
    {
        using var session = NewSession();
        var unknown = LearningCandidateId.New();

        var read = await session.Store.GetAsync(unknown, TestContext.Current.CancellationToken);
        var approved = await session.Store.ApproveAsync(unknown, now, TestContext.Current.CancellationToken);
        var rejected = await session.Store.RejectAsync(unknown, now, null, TestContext.Current.CancellationToken);

        read.ShouldBeNull();
        approved.ShouldBeNull();
        rejected.ShouldBeNull();
    }

    private static LearningCandidateId Id(LearningCandidateView candidate)
    {
        return new LearningCandidateId(candidate.Id);
    }

    private static LearningSuggestion Suggestion(
        string topic = "build.dotnet",
        string observation = "three runs failed on the same cold-cache test",
        string proposedRule = "run the warmup script before dotnet test")
    {
        return new LearningSuggestion(projectId, topic, observation, proposedRule, "worker:1");
    }

    private static Session NewSession()
    {
        var options = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase($"learning-store-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new Session(new EfLearningCandidateStore(new TestDbContextFactory(options)));
    }

    private sealed class Session(EfLearningCandidateStore store) : IDisposable
    {
        public EfLearningCandidateStore Store { get; } = store;

        public void Dispose()
        {
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<MemoryDbContext> options) : IDbContextFactory<MemoryDbContext>
    {
        public MemoryDbContext CreateDbContext()
        {
            return new MemoryDbContext(options);
        }
    }
}
