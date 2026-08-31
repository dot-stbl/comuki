using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Application.Validation;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using NSubstitute;
using Shouldly;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="ClaimWorkItemHandler"/> wiring: validation runs before the port,
/// the lease ttl comes from <see cref="LeaseOptions"/> on the injected clock,
/// and an empty queue is a null result, not an error.
/// </summary>
public sealed class ClaimWorkItemHandlerShould
{
    private static readonly WorkItemLabels labels = new("ghcr.io/comuki/worker@sha256:9f86d0", "refs/heads/main", "implement");
    private static readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid command, when HandleAsync is called, then it claims with a lease of the configured ttl")]
    public async Task ClaimWithConfiguredLeaseTtlAsync()
    {
        var clock = new FakeTimeProvider();
        var leaseOptions = OptionsFactory.Create(new LeaseOptions { LeaseTtl = TimeSpan.FromMinutes(5) });
        var queue = Substitute.For<IWorkItemQueue>();
        var claimed = new ClaimedWorkItem(Guid.CreateVersion7(), RunId.New(), "implement", /*lang=json,strict*/ """{"goal":"x"}""", now.AddMinutes(5), 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = queue.ClaimAsync(Arg.Any<WorkerId>(), Arg.Any<WorkItemLabels>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), cancellationToken)
            .Returns(claimed);
        var handler = new ClaimWorkItemHandler(new ClaimWorkItemValidator(), queue, clock, leaseOptions);
        var workerId = WorkerId.New();
        var command = new ClaimWorkItemCommand(workerId, labels);

        var result = await handler.HandleAsync(command, cancellationToken);

        result.ShouldBe(claimed);
        _ = await queue.Received(1).ClaimAsync(
            workerId,
            labels,
            now.AddMinutes(5),
            now,
            cancellationToken);
    }

    [Fact(DisplayName = "Given labels that fail validation, when HandleAsync is called, then it throws and never touches the queue")]
    public async Task RejectInvalidLabelsBeforeQueueAsync()
    {
        var queue = Substitute.For<IWorkItemQueue>();
        var handler = new ClaimWorkItemHandler(
            new ClaimWorkItemValidator(), queue, new FakeTimeProvider(), OptionsFactory.Create(new LeaseOptions()));

        _ = await Should.ThrowAsync<ValidationException>(
            () => handler.HandleAsync(new ClaimWorkItemCommand(WorkerId.New(), new WorkItemLabels("", "refs/heads/main", "implement")), TestContext.Current.CancellationToken));
        _ = await queue.DidNotReceiveWithAnyArgs().ClaimAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an empty queue, when HandleAsync is called, then it returns null")]
    public async Task ReturnNullWhenQueueEmptyAsync()
    {
        var queue = Substitute.For<IWorkItemQueue>();
        _ = queue.ClaimAsync(Arg.Any<WorkerId>(), Arg.Any<WorkItemLabels>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), TestContext.Current.CancellationToken)
            .Returns((ClaimedWorkItem?)null);
        var handler = new ClaimWorkItemHandler(
            new ClaimWorkItemValidator(), queue, new FakeTimeProvider(), OptionsFactory.Create(new LeaseOptions()));

        var result = await handler.HandleAsync(new ClaimWorkItemCommand(WorkerId.New(), labels), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }
}

/// <summary>
/// Deterministic clock for lease-ttl tests — the handler reads time
/// exclusively through the injected <see cref="TimeProvider"/>.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration)
    {
        utcNow = utcNow.Add(duration);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
