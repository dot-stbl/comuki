using Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="ClaimMergeBatchHandler"/> wiring: a Pending batch is
/// claimed and persisted. An unknown batch id returns null. The
/// domain precondition (Pending) is covered in the aggregate test
/// <c>MergeBatchShould</c>.
/// </summary>
public sealed class ClaimMergeBatchHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a pending batch, when HandleAsync is called, then it delegates to batch.Claim + SaveAsync")]
    public async Task ClaimTransitionsAndPersistsAsync()
    {
        var batchId = Guid.CreateVersion7();
        var batch = MergeBatch.Create("release-train-q3", ["https://example.com/pr/1"], now);
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(batchId, Arg.Any<CancellationToken>()).Returns(batch);
        var handler = new ClaimMergeBatchHandler(store, new ClaimMergeBatchValidator(), NullLogger<ClaimMergeBatchHandler>.Instance);

        var view = await handler.HandleAsync(
            new ClaimMergeBatchCommand(batchId),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Status.ShouldBe(MergeBatchStatus.InProgress);
        await store.Received(1).SaveAsync(
            Arg.Is<MergeBatch>(static updated => updated.Status == MergeBatchStatus.InProgress),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unknown batch id, when HandleAsync is called, then it returns null")]
    public async Task ClaimReturnsNullForUnknownBatchAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeBatch?)null);
        var handler = new ClaimMergeBatchHandler(store, new ClaimMergeBatchValidator(), NullLogger<ClaimMergeBatchHandler>.Instance);

        var view = await handler.HandleAsync(
            new ClaimMergeBatchCommand(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}
