using Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="AbandonMergeBatchHandler"/> wiring: validation rejects an
/// Abandon without a reason; an unknown batch id returns null. The
/// domain preconditions (Pending or InProgress) are covered in the
/// aggregate test <c>MergeBatchShould</c>.
/// </summary>
public sealed class AbandonMergeBatchHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an Abandon without reason, when HandleAsync is called, then it throws and never touches the store")]
    public async Task RejectAbandonWithoutReasonAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        var handler = new AbandonMergeBatchHandler(store, new AbandonMergeBatchValidator(), new MergeBatchFakeTimeProvider(now), NullLogger<AbandonMergeBatchHandler>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => handler.HandleAsync(
                new AbandonMergeBatchCommand(Guid.CreateVersion7(), Reason: " "),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().FindByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown batch id, when HandleAsync is called, then it returns null")]
    public async Task AbandonReturnsNullForUnknownBatchAsync()
    {
        var store = Substitute.For<IMergeBatchStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeBatch?)null);
        var handler = new AbandonMergeBatchHandler(store, new AbandonMergeBatchValidator(), new MergeBatchFakeTimeProvider(now), NullLogger<AbandonMergeBatchHandler>.Instance);

        var view = await handler.HandleAsync(
            new AbandonMergeBatchCommand(Guid.CreateVersion7(), "stale batch"),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}
