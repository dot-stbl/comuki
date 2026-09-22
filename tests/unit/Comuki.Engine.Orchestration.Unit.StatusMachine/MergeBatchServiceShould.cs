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
/// <see cref="MergeBatchService"/> read-side wiring: create delegates
/// to the store and projects the new batch through
/// <see cref="MergeBatchView"/>. Transition actions live in per-verb
/// handler tests. Mirrors <see cref="MergeQueueServiceShould"/> in
/// shape.
/// </summary>
public sealed class MergeBatchServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid create command, when CreateAsync is called, then it persists a Pending batch")]
    public async Task CreatePersistsPendingBatchAsync()
    {
        var clock = new MergeBatchFakeTimeProvider(now);
        var store = Substitute.For<IMergeBatchStore>();
        var service = new MergeBatchService(store, new MergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

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
        var service = new MergeBatchService(store, new MergeBatchValidator(), clock, NullLogger<MergeBatchService>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                new CreateMergeBatchCommand(
                    " ",
                    ["https://example.com/pr/1"]),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
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
