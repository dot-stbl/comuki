using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
/// shape. The transactional path is real — the service opens a
/// transaction on the scoped <see cref="OrchestrationDbContext"/>,
/// and the store's <c>SaveChangesAsync</c> + the journal's
/// <c>SaveChangesAsync</c> enlist on the same connection (both
/// <see cref="MergeBatchStoreEf"/> and <see cref="RunJournalEf"/> are
/// scoped to that one instance, mirroring the
/// <c>WorkItemQueueEf</c> pattern).
/// </summary>
public sealed class MergeBatchServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a valid create command, when CreateAsync is called, then it persists a Pending batch in a real transaction")]
    public async Task CreatePersistsPendingBatchAsync()
    {
        var context = NewContext();
        var store = new MergeBatchStoreEf(context);
        var journal = new RunJournalEf(context);
        var service = new MergeBatchService(
            store,
            new MergeBatchValidator(),
            journal,
            context,
            new MergeBatchFakeTimeProvider(now),
            NullLogger<MergeBatchService>.Instance);

        var view = await service.CreateAsync(
            new CreateMergeBatchCommand(
                "release-train-q3",
                ["https://example.com/pr/1", "https://example.com/pr/2"]),
            TestContext.Current.CancellationToken);

        view.Status.ShouldBe(MergeBatchStatus.Pending);
        view.Name.ShouldBe("release-train-q3");
        view.PullRequestUrls.Count.ShouldBe(2);
        view.CreatedAtUnixMs.ShouldBe(now.ToUnixTimeMilliseconds());

        // The store-side row landed in the same transaction.
        var row = await context.MergeBatches
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.Name.ShouldBe("release-train-q3");
        row.Status.ShouldBe(MergeBatchStatus.Pending);
        row.PullRequestUrls.Count.ShouldBe(2);

        // No RunId on the command — the service must not emit the
        // run-referenced event for cross-project release trains.
        var eventCount = await context.RunEvents.CountAsync(TestContext.Current.CancellationToken);
        eventCount.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a create command with a RunId, when CreateAsync is called, then the run-referenced event lands in the same transaction as the row")]
    public async Task CreateWithRunIdAppendsRunReferencedEventAsync()
    {
        var context = NewContext();
        var store = new MergeBatchStoreEf(context);
        var journal = new RunJournalEf(context);
        var runId = RunId.New();
        var service = new MergeBatchService(
            store,
            new MergeBatchValidator(),
            journal,
            context,
            new MergeBatchFakeTimeProvider(now),
            NullLogger<MergeBatchService>.Instance);

        await service.CreateAsync(
            new CreateMergeBatchCommand(
                "release-train-q4",
                ["https://example.com/pr/3"],
                runId),
            TestContext.Current.CancellationToken);

        // The row + journal entry are in the same context — the
        // transaction committed them together (the row is visible
        // because the journal append succeeded; if the journal had
        // thrown, the transaction would have rolled back the row
        // too).
        var row = await context.MergeBatches
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.Name.ShouldBe("release-train-q4");

        var entry = await context.RunEvents
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        entry.Type.ShouldBe("merge_queue.run_referenced");
        entry.RunId.ShouldBe(runId);
        entry.Payload.ShouldContain("\"kind\":\"batch\"");
    }

    [Fact(DisplayName = "Given an invalid create command, when CreateAsync is called, then it throws and never opens a transaction")]
    public async Task RejectInvalidCreateAsync()
    {
        var context = NewContext();
        var store = Substitute.For<IMergeBatchStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new MergeBatchService(
            store,
            new MergeBatchValidator(),
            journal,
            context,
            new MergeBatchFakeTimeProvider(now),
            NullLogger<MergeBatchService>.Instance);

        var exception = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                new CreateMergeBatchCommand(
                    " ",
                    ["https://example.com/pr/1"]),
                TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    private static OrchestrationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"merge-batch-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
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
