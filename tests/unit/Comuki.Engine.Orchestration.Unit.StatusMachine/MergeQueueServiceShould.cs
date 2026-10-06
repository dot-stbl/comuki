using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeQueueService"/> read-side wiring: enqueue delegates
/// to the store and projects the new entry through
/// <see cref="MergeQueueEntryView"/>; claim-next returns null when the
/// queue is empty. Transition actions live in per-verb handler tests.
/// </summary>
public sealed class MergeQueueServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid enqueue command, when HandleAsync is called, then it persists a Pending entry")]
    public async Task EnqueuePersistsPendingEntryAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        var journal = Substitute.For<IRunJournal>();
        var projectId = ProjectId.New();
        var validator = new MergeQueueValidator();
        var service = new MergeQueueService(store, validator, journal, clock, NullLogger<MergeQueueService>.Instance);

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
        await store.Received(1).AddAsync(
            Arg.Is<MergeQueueEntry>(entry =>
                entry.BranchName == "feature/merge-queue"
                && entry.Status == MergeQueueStatus.Pending
                && entry.ProjectId == projectId),
            TestContext.Current.CancellationToken);
        // No RunId on the command — the service must not emit the
        // run-referenced event for cross-project release-train entries.
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an enqueue command with a RunId, when HandleAsync is called, then it appends the run-referenced event in the same scope")]
    public async Task EnqueueWithRunIdAppendsRunReferencedEventAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        var journal = Substitute.For<IRunJournal>();
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var service = new MergeQueueService(store, new MergeQueueValidator(), journal, clock, NullLogger<MergeQueueService>.Instance);

        await service.EnqueueAsync(
            new EnqueueMergeRequestCommand(
                projectId,
                "feature/with-run",
                "https://github.com/comuki/comuki.orchestrator/pull/99",
                ConflictResolution.None,
                "from a run",
                runId),
            TestContext.Current.CancellationToken);

        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(entry =>
                entry.Type == "merge_queue.run_referenced"
                && entry.RunId == runId),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given invalid enqueue, when HandleAsync is called, then it throws and never touches the store")]
    public async Task RejectInvalidEnqueueAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new MergeQueueService(store, new MergeQueueValidator(), journal, clock, NullLogger<MergeQueueService>.Instance);

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
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        store.ClaimNextAsync(Arg.Any<ProjectId?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((MergeQueueEntry?)null);
        var journal = Substitute.For<IRunJournal>();
        var service = new MergeQueueService(store, new MergeQueueValidator(), journal, clock, NullLogger<MergeQueueService>.Instance);

        var view = await service.ClaimNextAsync(null, "operator-alice", TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}

/// <summary>Deterministic clock for the merge-queue service tests.</summary>
internal sealed class MergeQueueFakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly DateTimeOffset utcNow = initial;

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
