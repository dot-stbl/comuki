using Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="MergeMergeQueueHandler"/> wiring: an unknown entry id
/// returns null (the handler is the read-through dispatch — the domain
/// mutator throws on an InProgress precondition, covered in the
/// aggregate test <c>MergeQueueEntryShould</c>).
/// </summary>
public sealed class MergeMergeQueueHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an unknown entry id, when HandleAsync is called, then it returns null")]
    public async Task MergeReturnsNullForUnknownEntryAsync()
    {
        var store = Substitute.For<IMergeQueueStore>();
        store.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MergeQueueEntry?)null);
        var handler = new MergeMergeQueueHandler(store, new MergeMergeQueueValidator(), new MergeQueueFakeTimeProvider(now), NullLogger<MergeMergeQueueHandler>.Instance);

        var view = await handler.HandleAsync(
            new MergeMergeQueueCommand(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }
}
