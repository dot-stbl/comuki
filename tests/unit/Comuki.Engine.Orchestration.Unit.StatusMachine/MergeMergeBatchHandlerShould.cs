using Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeMergeBatchHandler"/> wiring: an in-progress batch
/// is merged with a timestamp and persisted; an unknown batch id
/// returns null.
/// </summary>
public sealed class MergeMergeBatchHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an in-progress batch, when HandleAsync is called, then it is Merged with timestamp")]
    public async Task MergeTransitionsAndPersistsAsync()
    {
        var batchId = Guid.CreateVersion7();
        var batch = MergeBatch.Create("release-train-q3", ["https://example.com/pr/1"], now);
        batch.Claim();
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(batchId, Arg.Any<CancellationToken>()).Returns(batch);
        var clock = new MergeBatchFakeTimeProvider(now);
        var handler = new MergeMergeBatchHandler(store, new MergeMergeBatchValidator(), clock, NullLogger<MergeMergeBatchHandler>.Instance);

        var view = await handler.HandleAsync(
            new MergeMergeBatchCommand(batchId),
            TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Status.ShouldBe(MergeBatchStatus.Merged);
        view.MergedAtUnixMs.ShouldBe(now.ToUnixTimeMilliseconds());
    }

    [Fact(DisplayName = "Given an unknown batch id, when HandleAsync is called, then it returns null")]
    public async Task MergeReturnsNullForUnknownBatchAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeBatch?)null);
        var clock = new MergeBatchFakeTimeProvider(now);
        var handler = new MergeMergeBatchHandler(store, new MergeMergeBatchValidator(), clock, NullLogger<MergeMergeBatchHandler>.Instance);

        var view = await handler.HandleAsync(
            new MergeMergeBatchCommand(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}
