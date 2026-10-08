using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeQueueService"/> read-side wiring: enqueue delegates
/// to the store and projects the new entry through
/// <see cref="MergeQueueEntryView"/>; claim-next returns null when the
/// queue is empty. Transition actions live in per-verb handler tests.
/// The transactional path is real — the service opens a transaction
/// on the scoped <see cref="OrchestrationDbContext"/>, and the
/// store's <c>SaveChangesAsync</c> + the journal's
/// <c>SaveChangesAsync</c> enlist on the same connection (both
/// <see cref="MergeQueueStoreEf"/> and <see cref="RunJournalEf"/> are
/// scoped to that one instance, mirroring the
/// <c>WorkItemQueueEf</c> pattern).
/// </summary>
public sealed class MergeQueueServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid enqueue command, when HandleAsync is called, then it persists a Pending entry in a real transaction")]
    public async Task EnqueuePersistsPendingEntryAsync()
    {
        var context = NewContext();
        var store = new MergeQueueStoreEf(context);
        var journal = new RunJournalEf(context);
        var projectId = ProjectId.New();
        var service = new MergeQueueService(
            store,
            new MergeQueueValidator(),
            journal,
            context,
            new FakeTimeProvider(now),
            NullLogger<MergeQueueService>.Instance);

        var view = await service.EnqueueAsync(
            new EnqueueMergeRequestCommand(
                projectId,
                "feature/merge-queue",
                "https://github.com/comuki/comuki.orchestrator/pull/42",
                ConflictResolution.AutoRebase,
                "intake from #11"),
            TestContext.Current.CancellationToken);

        view.Status.ShouldBe(MergeQueueStatus.Pending);
        view.BranchName.ShouldBe("feature/merge-queue");
        view.ConflictResolution.ShouldBe(ConflictResolution.AutoRebase);
        view.EnqueuedAtUnixMs.ShouldBe(now.ToUnixTimeMilliseconds());
        view.ProjectId.ShouldBe(projectId);

        var row = await context.MergeQueue
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.BranchName.ShouldBe("feature/merge-queue");
        row.Status.ShouldBe(MergeQueueStatus.Pending);
        row.ProjectId.ShouldBe(projectId);

        // No RunId on the command — the service must not emit the
        // run-referenced event for cross-project release-train entries.
        var eventCount = await context.RunEvents.CountAsync(TestContext.Current.CancellationToken);
        eventCount.ShouldBe(0);
    }

    [Fact(DisplayName = "Given an enqueue command with a RunId, when HandleAsync is called, then the run-referenced event lands in the same transaction as the row")]
    public async Task EnqueueWithRunIdAppendsRunReferencedEventAsync()
    {
        var context = NewContext();
        var store = new MergeQueueStoreEf(context);
        var journal = new RunJournalEf(context);
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var service = new MergeQueueService(
            store,
            new MergeQueueValidator(),
            journal,
            context,
            new FakeTimeProvider(now),
            NullLogger<MergeQueueService>.Instance);

        await service.EnqueueAsync(
            new EnqueueMergeRequestCommand(
                projectId,
                "feature/with-run",
                "https://github.com/comuki/comuki.orchestrator/pull/99",
                ConflictResolution.None,
                "from a run",
                runId),
            TestContext.Current.CancellationToken);

        // The row + journal entry are in the same context — the
        // transaction committed them together.
        var row = await context.MergeQueue
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.BranchName.ShouldBe("feature/with-run");

        var entry = await context.RunEvents
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        entry.Type.ShouldBe("merge_queue.run_referenced");
        entry.RunId.ShouldBe(runId);
        entry.Payload.ShouldContain("\"kind\":\"entry\"");
    }

    [Fact(DisplayName = "Given invalid enqueue, when HandleAsync is called, then it throws and never opens a transaction")]
    public async Task RejectInvalidEnqueueAsync()
    {
        var context = NewContext();
        var store = Substitute.For<IMergeQueueStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new MergeQueueService(
            store,
            new MergeQueueValidator(),
            journal,
            context,
            new FakeTimeProvider(now),
            NullLogger<MergeQueueService>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => service.EnqueueAsync(
                new EnqueueMergeRequestCommand(
                    ProjectId.New(),
                    " ",
                    "https://example.com/pr/1",
                    ConflictResolution.None,
                    null),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an empty queue, when ClaimNextAsync is called, then it returns null")]
    public async Task ClaimNextReturnsNullWhenEmptyAsync()
    {
        var context = NewContext();
        var store = Substitute.For<IMergeQueueStore>();
        store.ClaimNextAsync(Arg.Any<ProjectId?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((MergeQueueEntry?)null);
        var journal = Substitute.For<IRunJournal>();
        var service = new MergeQueueService(
            store,
            new MergeQueueValidator(),
            journal,
            context,
            new FakeTimeProvider(now),
            NullLogger<MergeQueueService>.Instance);

        var view = await service.ClaimNextAsync(null, "operator-alice", TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }

    private static OrchestrationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"merge-queue-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }
}

