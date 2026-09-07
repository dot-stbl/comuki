using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeBatchService"/> wiring: create delegates to the
/// store and projects the new batch through
/// <see cref="MergeBatchView"/>; update by action translates to the
/// right domain mutator and round-trips the view. Mirrors
/// <see cref="MergeQueueServiceShould"/> in shape.
/// </summary>
public sealed class MergeBatchServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid create command, when CreateAsync is called, then it persists a Pending batch")]
    public async Task CreatePersistsPendingBatchAsync()
    {
        var clock = new MergeBatchFakeTimeProvider(now);
        var store = Substitute.For<IMergeBatchStore>();
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

        var view = await service.CreateAsync(
            new CreateMergeBatchCommand(
                "release-train-q3",
                ["https://example.com/pr/1", "https://example.com/pr/2"]),
            TestContext.Current.CancellationToken);

        view.Status.ShouldBe(MergeBatchStatus.Pending);
        view.Name.ShouldBe("release-train-q3");
        view.PullRequestUrls.Count.ShouldBe(2);
        view.CreatedAtUnixMs.ShouldBe(now.ToUnixTimeMilliseconds());
        await store.Received(1).AddAsync(
            Arg.Is<MergeBatch>(static batch =>
                batch.Name == "release-train-q3"
                && batch.Status == MergeBatchStatus.Pending
                && batch.PullRequestUrls.Count == 2),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an invalid create command, when CreateAsync is called, then it throws and never touches the store")]
    public async Task RejectInvalidCreateAsync()
    {
        var clock = new MergeBatchFakeTimeProvider(now);
        var store = Substitute.For<IMergeBatchStore>();
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                new CreateMergeBatchCommand(
                    " ",
                    ["https://example.com/pr/1"]),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a pending batch, when UpdateAsync(Claim) is called, then it delegates to batch.Claim + SaveAsync")]
    public async Task ClaimActionTransitionsAndPersistsAsync()
    {
        var batchId = Guid.CreateVersion7();
        var batch = MergeBatch.Create("release-train-q3", ["https://example.com/pr/1"], now);
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(batchId, Arg.Any<CancellationToken>()).Returns(batch);
        var clock = new MergeBatchFakeTimeProvider(now);
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

        var view = await service.UpdateAsync(
            new UpdateMergeBatchCommand(batchId, MergeBatchAction.Claim, Reason: null),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view.Status.ShouldBe(MergeBatchStatus.InProgress);
        await store.Received(1).SaveAsync(
            Arg.Is<MergeBatch>(static updated => updated.Status == MergeBatchStatus.InProgress),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an in-progress batch, when UpdateAsync(Merge) is called, then it is Merged with timestamp")]
    public async Task MergeActionTransitionsAndPersistsAsync()
    {
        var batchId = Guid.CreateVersion7();
        var batch = MergeBatch.Create("release-train-q3", ["https://example.com/pr/1"], now);
        batch.Claim();
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(batchId, Arg.Any<CancellationToken>()).Returns(batch);
        var clock = new MergeBatchFakeTimeProvider(now);
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

        var view = await service.UpdateAsync(
            new UpdateMergeBatchCommand(batchId, MergeBatchAction.Merge, Reason: null),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view.Status.ShouldBe(MergeBatchStatus.Merged);
        view.MergedAtUnixMs.ShouldBe(now.ToUnixTimeMilliseconds());
    }

    [Fact(DisplayName = "Given an Abandon without reason, when UpdateAsync is called, then it throws and never touches the store")]
    public async Task RejectAbandonWithoutReasonAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), new MergeBatchFakeTimeProvider(now), NullLogger<MergeBatchService>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => service.UpdateAsync(
                new UpdateMergeBatchCommand(Guid.CreateVersion7(), MergeBatchAction.Abandon, Reason: null),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().FindByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown batch id, when UpdateAsync is called, then it returns null")]
    public async Task UpdateReturnsNullForUnknownBatchAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeBatch?)null);
        var service = new MergeBatchService(store, new MergeBatchValidator(), new UpdateMergeBatchValidator(), new MergeBatchFakeTimeProvider(now), NullLogger<MergeBatchService>.Instance);

        var view = await service.UpdateAsync(
            new UpdateMergeBatchCommand(Guid.CreateVersion7(), MergeBatchAction.Merge, Reason: null),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}

/// <summary>Deterministic clock for the merge-batch service tests.</summary>
internal sealed class MergeBatchFakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly DateTimeOffset utcNow = initial;

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
