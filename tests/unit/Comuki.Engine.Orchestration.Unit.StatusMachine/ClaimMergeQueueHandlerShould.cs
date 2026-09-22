using Comuki.Engine.Orchestration.Application.MergeQueue.Claim;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="ClaimMergeQueueHandler"/> wiring: a Pending entry is
/// claimed for the operator and persisted; an empty operator id is
/// rejected at validation; an unknown entry id returns null.
/// </summary>
public sealed class ClaimMergeQueueHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a Pending entry, when HandleAsync is called, then it delegates to entry.Claim + SaveAsync")]
    public async Task ClaimTransitionsAndPersistsAsync()
    {
        var entryId = Guid.CreateVersion7();
        var projectId = Shared.Kernel.Ids.ProjectId.New();
        var entry = MergeQueueEntry.Create(projectId, "feature/x", "https://example.com/pr/2", ConflictResolution.None, null, now);
        var store = Substitute.For<IMergeQueueStore>();
        store.FindByIdAsync(entryId, Arg.Any<CancellationToken>()).Returns(entry);
        var handler = new ClaimMergeQueueHandler(store, new ClaimMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<ClaimMergeQueueHandler>.Instance);

        var view = await handler.HandleAsync(
            new ClaimMergeQueueCommand(entryId, "operator-bob"),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Status.ShouldBe(MergeQueueStatus.InProgress);
        view.ClaimedBy.ShouldBe("operator-bob");
        await store.Received(1).SaveAsync(
            Arg.Is<MergeQueueEntry>(static updated => updated.Status == MergeQueueStatus.InProgress),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a Claim with empty operator id, when HandleAsync is called, then it throws and never touches the store")]
    public async Task RejectClaimWithoutOperatorIdAsync()
    {
        var store = Substitute.For<IMergeQueueStore>();
        var handler = new ClaimMergeQueueHandler(store, new ClaimMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<ClaimMergeQueueHandler>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => handler.HandleAsync(
                new ClaimMergeQueueCommand(Guid.CreateVersion7(), " "),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().FindByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown entry id, when HandleAsync is called, then it returns null")]
    public async Task ClaimReturnsNullForUnknownEntryAsync()
    {
        var store = Substitute.For<IMergeQueueStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeQueueEntry?)null);
        var handler = new ClaimMergeQueueHandler(store, new ClaimMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<ClaimMergeQueueHandler>.Instance);

        var view = await handler.HandleAsync(
            new ClaimMergeQueueCommand(Guid.CreateVersion7(), "operator-bob"),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}
