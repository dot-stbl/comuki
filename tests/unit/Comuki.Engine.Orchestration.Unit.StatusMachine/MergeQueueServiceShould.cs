using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeQueueService"/> wiring: enqueue delegates to the
/// store and projects the new entry through <see cref="MergeQueueEntryView"/>;
/// claim-next returns null when the queue is empty; update by action
/// translates to the right domain mutator and round-trips the view.
/// </summary>
public sealed class MergeQueueServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid enqueue command, when HandleAsync is called, then it persists a Pending entry")]
    public async Task EnqueuePersistsPendingEntryAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        var projectId = ProjectId.New();
        var validator = new MergeQueueValidator();
        var service = new MergeQueueService(store, validator, new UpdateMergeQueueValidator(), clock, NullLogger<MergeQueueService>.Instance);

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
    }

    [Fact(DisplayName = "Given invalid enqueue, when HandleAsync is called, then it throws and never touches the store")]
    public async Task RejectInvalidEnqueueAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        var service = new MergeQueueService(store, new MergeQueueValidator(), new UpdateMergeQueueValidator(), clock, NullLogger<MergeQueueService>.Instance);

        _ = await Should.ThrowAsync<ValidationException>(
            () => service.EnqueueAsync(
                new EnqueueMergeRequestCommand(
                    ProjectId.New(),
                    " ",
                    "https://example.com/pr/1",
                    ConflictResolution.None,
                    null),
                TestContext.Current.CancellationToken));

        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an empty queue, when ClaimNextAsync is called, then it returns null")]
    public async Task ClaimNextReturnsNullWhenEmptyAsync()
    {
        var clock = new MergeQueueFakeTimeProvider(now);
        var store = Substitute.For<IMergeQueueStore>();
        store.ClaimNextAsync(Arg.Any<ProjectId?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((MergeQueueEntry?)null);
        var service = new MergeQueueService(store, new MergeQueueValidator(), new UpdateMergeQueueValidator(), clock, NullLogger<MergeQueueService>.Instance);

        var view = await service.ClaimNextAsync(null, "operator-alice", TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a queued entry, when UpdateAsync(Claim) is called, then it delegates to entry.Claim + SaveAsync")]
    public async Task ClaimActionTransitionsAndPersistsAsync()
    {
        var entryId = Guid.CreateVersion7();
        var projectId = ProjectId.New();
        var entry = MergeQueueEntry.Create(projectId, "feature/x", "https://example.com/pr/2", ConflictResolution.None, null, now);
        // Force the entry id to a known value for the Arg matcher.
        var store = Substitute.For<IMergeQueueStore>();
        store.FindByIdAsync(entryId, Arg.Any<CancellationToken>()).Returns(entry);
        var clock = new MergeQueueFakeTimeProvider(now);
        var service = new MergeQueueService(store, new MergeQueueValidator(), new UpdateMergeQueueValidator(), clock, NullLogger<MergeQueueService>.Instance);

        var view = await service.UpdateAsync(
            new UpdateMergeQueueCommand(entryId, MergeQueueAction.Claim, "operator-bob", null, null),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view.Status.ShouldBe(MergeQueueStatus.InProgress);
        view.ClaimedBy.ShouldBe("operator-bob");
        await store.Received(1).SaveAsync(
            Arg.Is<MergeQueueEntry>(static updated => updated.Status == MergeQueueStatus.InProgress),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a Claim with empty operator id, when UpdateAsync is called, then it throws and never touches the store")]
    public async Task RejectClaimWithoutOperatorIdAsync()
    {
        var store = Substitute.For<IMergeQueueStore>();
        var service = new MergeQueueService(store, new MergeQueueValidator(), new UpdateMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<MergeQueueService>.Instance);

        _ = await Should.ThrowAsync<ValidationException>(
            () => service.UpdateAsync(
                new UpdateMergeQueueCommand(Guid.CreateVersion7(), MergeQueueAction.Claim, " ", null, null),
                TestContext.Current.CancellationToken));

        await store.DidNotReceiveWithAnyArgs().FindByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown entry id, when UpdateAsync is called, then it returns null")]
    public async Task UpdateReturnsNullForUnknownEntryAsync()
    {
        var store = Substitute.For<IMergeQueueStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeQueueEntry?)null);
        var service = new MergeQueueService(store, new MergeQueueValidator(), new UpdateMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<MergeQueueService>.Instance);

        var view = await service.UpdateAsync(
            new UpdateMergeQueueCommand(Guid.CreateVersion7(), MergeQueueAction.Merge, null, null, null),
            TestContext.Current.CancellationToken);

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
