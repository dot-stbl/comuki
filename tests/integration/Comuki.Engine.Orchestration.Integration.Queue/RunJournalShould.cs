using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// <see cref="IRunJournal"/> against real Postgres: appends round-trip, the
/// timeline pages oldest-first, and paging arguments are guarded.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="QueueIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(QueueIntegrationCollection))]
public sealed class RunJournalShould(PostgresCollectionFixture postgres) : QueueDatabase(postgres)
{
    private static readonly DateTimeOffset baseTime = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given journal entries, when the timeline is read, then pages come back oldest first")]
    public async Task PageTimelineOldestFirstAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        var run = await SeedRunAsync();
        var runId = run.Id;

        for (var index = 0; index < 5; index++)
        {
            await journal.AppendAsync(new RunEventEntry(
                Guid.CreateVersion7(),
                runId,
                RunEventTypes.WorkerReported,
                $$"""{"seq":{{index}}}""",
                baseTime.AddSeconds(index)), cancellationToken);
        }

        var firstPage = await journal.ReadTimelineAsync(runId, page: 1, pageSize: 2, cancellationToken);
        var secondPage = await journal.ReadTimelineAsync(runId, page: 2, pageSize: 2, cancellationToken);
        var lastPage = await journal.ReadTimelineAsync(runId, page: 3, pageSize: 2, cancellationToken);

        firstPage.Count.ShouldBe(2);
        secondPage.Count.ShouldBe(2);
        lastPage.Count.ShouldBe(1);

        using var firstPayload = JsonDocument.Parse(firstPage[0].PayloadJson);
        firstPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(0);
        using var secondPayload = JsonDocument.Parse(secondPage[0].PayloadJson);
        secondPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(2);
        using var lastPayload = JsonDocument.Parse(lastPage[0].PayloadJson);
        lastPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(4);
    }

    [Fact(DisplayName = "Given a run with no journal, when the timeline is read, then the page is empty")]
    public async Task ReturnEmptyTimelineForUnknownRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();

        var timeline = await journal.ReadTimelineAsync(RunId.New(), page: 1, pageSize: 10, cancellationToken);

        timeline.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given timelines of two runs, when one is read, then the other run's entries stay out")]
    public async Task IsolateTimelinesBetweenRunsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        var runA = await SeedRunAsync();
        var runB = await SeedRunAsync();

        await journal.AppendAsync(new RunEventEntry(Guid.CreateVersion7(), runA.Id, RunEventTypes.RunStatusChanged, /*lang=json,strict*/ """{"seq":"a"}""", baseTime), cancellationToken);
        await journal.AppendAsync(new RunEventEntry(Guid.CreateVersion7(), runB.Id, RunEventTypes.RunStatusChanged, /*lang=json,strict*/ """{"seq":"b"}""", baseTime.AddSeconds(1)), cancellationToken);

        var timelineA = await journal.ReadTimelineAsync(runA.Id, page: 1, pageSize: 10, cancellationToken);

        var entry = timelineA.ShouldHaveSingleItem();
        using var payload = JsonDocument.Parse(entry.PayloadJson);
        payload.RootElement.GetProperty("seq").GetString().ShouldBe("a");
    }

    [Fact(DisplayName = "Given a non-positive page or page size, when the timeline is read, then it throws")]
    public async Task RejectInvalidPagingAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();

        await Should.ThrowAsync<ArgumentException>(() => journal.ReadTimelineAsync(RunId.New(), page: 0, pageSize: 10, cancellationToken));
        await Should.ThrowAsync<ArgumentException>(() => journal.ReadTimelineAsync(RunId.New(), page: 1, pageSize: 0, cancellationToken));
    }

    /// <summary>Seeds a run with <paramref name="count"/> Queued work items —
    /// for the terminal-reconciliation race tests below.</summary>
    private async Task<(Run Run, List<WorkItem> Items)> SeedRunWithQueuedItemsAsync(int count)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = clock.GetUtcNow();
        var run = Run.Create(ProjectId.New(), now);
        var items = Enumerable.Range(0, count)
            .Select(index => WorkItem.Create(run.Id, "implement", Image, ProfilesRef, $$"""{"goal":"item {{index}}"}""", WorkItemStatus.Queued, now))
            .ToList();
        db.Runs.Add(run);
        db.WorkItems.AddRange(items);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (run, items);
    }

    [Fact(DisplayName = "Given a run's last two items, when two workers complete concurrently, then the run finalizes to Succeeded exactly once")]
    public async Task FinalizeExactlyOnceOnConcurrentCompletionsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (run, items) = await SeedRunWithQueuedItemsAsync(2);
        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerA = WorkerId.New();
        var workerB = WorkerId.New();
        var claimedA = await queueA.ClaimAsync(workerA, ImplementLabels, baseTime.AddMinutes(2), baseTime, cancellationToken);
        var claimedB = await queueB.ClaimAsync(workerB, ImplementLabels, baseTime.AddMinutes(2), baseTime, cancellationToken);
        claimedA.ShouldNotBeNull();
        claimedB.ShouldNotBeNull();

        // fire both completions without awaiting either first — genuine
        // concurrent transactions on separate connections/scopes
        var completeA = queueA.CompleteAsync(claimedA.WorkItemId, workerA, /*lang=json,strict*/ """{"summary":"a done"}""", baseTime.AddMinutes(1), cancellationToken);
        var completeB = queueB.CompleteAsync(claimedB.WorkItemId, workerB, /*lang=json,strict*/ """{"summary":"b done"}""", baseTime.AddMinutes(1), cancellationToken);
        var resultA = await completeA;
        var resultB = await completeB;

        resultA.ShouldBeTrue();
        resultB.ShouldBeTrue();
        var finalRun = await LoadRunAsync(run.Id);
        finalRun.Status.ShouldBe(RunStatus.Succeeded);
        var events = await LoadEventsAsync(run.Id);
        var runStatusEvents = events.Where(static runEvent => runEvent.Type == "run.status_changed").ToList();
        // activation (Queued -> Running on first claim) + finalization (Running -> Succeeded)
        runStatusEvents.Count.ShouldBe(2);
        var finalizations = runStatusEvents.Where(static runEvent =>
        {
            using var payload = JsonDocument.Parse(runEvent.Payload);
            return payload.RootElement.GetProperty("to").GetString() is "Succeeded" or "Failed";
        }).ToList();
        // the actual exactly-once invariant: only one finalization event
        finalizations.ShouldHaveSingleItem();

        var outboxMessages = await LoadOutboxMessagesAsync();
        var terminatedMessage = outboxMessages.ShouldHaveSingleItem();
        terminatedMessage.Type.ShouldBe(RunEventTypes.RunTerminatedV1);
        using var outboxPayload = JsonDocument.Parse(terminatedMessage.Payload);
        outboxPayload.RootElement.GetProperty("runId").GetGuid().ShouldBe(run.Id.Value);
        outboxPayload.RootElement.GetProperty("status").GetString().ShouldBe(finalRun.Status.ToString());
    }

    [Fact(DisplayName = "Given a run's last two items, when one fails and the other succeeds concurrently, then the run finalizes to Failed exactly once")]
    public async Task FinalizeExactlyOnceOnConcurrentCompleteAndFailAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (run, items) = await SeedRunWithQueuedItemsAsync(2);
        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerA = WorkerId.New();
        var workerB = WorkerId.New();
        var claimedA = await queueA.ClaimAsync(workerA, ImplementLabels, baseTime.AddMinutes(2), baseTime, cancellationToken);
        var claimedB = await queueB.ClaimAsync(workerB, ImplementLabels, baseTime.AddMinutes(2), baseTime, cancellationToken);
        claimedA.ShouldNotBeNull();
        claimedB.ShouldNotBeNull();

        var completeA = queueA.CompleteAsync(claimedA.WorkItemId, workerA, /*lang=json,strict*/ """{"summary":"a done"}""", baseTime.AddMinutes(1), cancellationToken);
        var failB = queueB.FailAsync(claimedB.WorkItemId, workerB, "boom", baseTime.AddMinutes(1), cancellationToken);
        var resultA = await completeA;
        var resultB = await failB;

        resultA.ShouldBeTrue();
        resultB.ShouldBeTrue();
        var finalRun = await LoadRunAsync(run.Id);
        finalRun.Status.ShouldBe(RunStatus.Failed);
        var events = await LoadEventsAsync(run.Id);
        var runStatusEvents = events.Where(static runEvent => runEvent.Type == "run.status_changed").ToList();
        // activation (Queued -> Running on first claim) + finalization (Running -> Failed)
        runStatusEvents.Count.ShouldBe(2);
        var finalizations = runStatusEvents.Where(static runEvent =>
        {
            using var payload = JsonDocument.Parse(runEvent.Payload);
            return payload.RootElement.GetProperty("to").GetString() is "Succeeded" or "Failed";
        }).ToList();
        // the actual exactly-once invariant: only one finalization event
        finalizations.ShouldHaveSingleItem();

        var outboxMessages = await LoadOutboxMessagesAsync();
        var terminatedMessage = outboxMessages.ShouldHaveSingleItem();
        terminatedMessage.Type.ShouldBe(RunEventTypes.RunTerminatedV1);
        using var outboxPayload = JsonDocument.Parse(terminatedMessage.Payload);
        outboxPayload.RootElement.GetProperty("runId").GetGuid().ShouldBe(run.Id.Value);
        outboxPayload.RootElement.GetProperty("status").GetString().ShouldBe(finalRun.Status.ToString());
    }

    [Fact(DisplayName = "Given a run's last two items, when one is completed while the other's lease is reaped-to-failed concurrently, then the run finalizes to Failed exactly once")]
    public async Task FinalizeExactlyOnceOnCompleteVersusReaperRaceAsync()
    {
        // MaxAttempts=2 makes a single-sweep concurrent race impractical: one
        // reap requeues B (attempt 1 < 2), not fail. Multi-cycle prep bumps B's
        // attempt to 2 so a final reap fails it; the actual race (completeA vs
        // reaper sweep on B) is still genuinely concurrent, mirroring tests 1/2.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (run, items) = await SeedRunWithQueuedItemsAsync(2);
        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var reaper = scopeB.ServiceProvider.GetRequiredService<LeaseReaper>();
        var workerA = WorkerId.New();
        var workerB = WorkerId.New();

        // Cycle 1: claim B with a short lease; first reap requeues it.
        var firstClaimB = await queueB.ClaimAsync(workerB, ImplementLabels, baseTime.AddSeconds(30), baseTime, cancellationToken);
        firstClaimB.ShouldNotBeNull();
        firstClaimB.Attempt.ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(31).Add(TimeSpan.FromSeconds(31))); // past B's first lease + ReapGrace
        var firstReap = await reaper.ReapAsync(cancellationToken);
        var firstReapB = firstReap.ShouldHaveSingleItem();
        firstReapB.WorkItemId.ShouldBe(firstClaimB.WorkItemId);
        firstReapB.MarkedFailed.ShouldBeFalse();
        (await LoadItemAsync(firstClaimB.WorkItemId)).ShouldNotBeNull().Status.ShouldBe(WorkItemStatus.Queued);

        // Cycle 2 prep: claim A with a long lease (so the reaper ignores it) and
        // re-claim B (attempt now bumps to 2).
        var claimedA = await queueA.ClaimAsync(workerA, ImplementLabels, clock.GetUtcNow().AddMinutes(10), clock.GetUtcNow(), cancellationToken);
        var claimedB = await queueB.ClaimAsync(workerB, ImplementLabels, clock.GetUtcNow().AddSeconds(30), clock.GetUtcNow(), cancellationToken);
        claimedA.ShouldNotBeNull();
        claimedB.ShouldNotBeNull();
        claimedB.Attempt.ShouldBe(2);

        // Advance past B's second lease + ReapGrace; A's 10-minute lease is still valid.
        clock.Advance(TimeSpan.FromSeconds(31).Add(TimeSpan.FromSeconds(31)));

        // Race: complete A and reap-fail B concurrently on separate scopes/connections.
        var completeA = queueA.CompleteAsync(claimedA.WorkItemId, workerA, /*lang=json,strict*/ """{"summary":"a done"}""", clock.GetUtcNow(), cancellationToken);
        var reapSweep = reaper.ReapAsync(cancellationToken);
        var resultA = await completeA;
        var reaped = await reapSweep;

        resultA.ShouldBeTrue();
        var reapedB = reaped.ShouldHaveSingleItem();
        reapedB.WorkItemId.ShouldBe(claimedB.WorkItemId);
        reapedB.MarkedFailed.ShouldBeTrue();

        var finalRun = await LoadRunAsync(run.Id);
        finalRun.Status.ShouldBe(RunStatus.Failed);
        var events = await LoadEventsAsync(run.Id);
        var runStatusEvents = events.Where(static runEvent => runEvent.Type == "run.status_changed").ToList();
        // activation (Queued -> Running on first claim) + finalization (Running -> Failed)
        runStatusEvents.Count.ShouldBe(2);
        var finalizations = runStatusEvents.Where(static runEvent =>
        {
            using var payload = JsonDocument.Parse(runEvent.Payload);
            return payload.RootElement.GetProperty("to").GetString() is "Succeeded" or "Failed";
        }).ToList();
        // the actual exactly-once invariant: only one finalization event
        finalizations.ShouldHaveSingleItem();

        var outboxMessages = await LoadOutboxMessagesAsync();
        var terminatedMessage = outboxMessages.ShouldHaveSingleItem();
        terminatedMessage.Type.ShouldBe(RunEventTypes.RunTerminatedV1);
        using var outboxPayload = JsonDocument.Parse(terminatedMessage.Payload);
        outboxPayload.RootElement.GetProperty("runId").GetGuid().ShouldBe(run.Id.Value);
        outboxPayload.RootElement.GetProperty("status").GetString().ShouldBe(finalRun.Status.ToString());
    }

    [Fact(DisplayName = "Given a run finalizes to a terminal status, when the process 'crashes' before any dispatch sweep, then a later dispatcher cycle still delivers the terminal outbox message")]
    public async Task TerminalOutboxMessageSurvivesACrashBeforeDispatchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var worker = WorkerId.New();
        var claimed = await queue.ClaimAsync(worker, ImplementLabels, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);
        claimed.ShouldNotBeNull();

        // Terminal transition commits (including the outbox row) — then the
        // "process crashes" before any dispatcher cycle runs: nothing below
        // calls DispatchAsync yet, mirroring a crash between commit and the
        // best-effort realtime broadcast.
        var completed = await queue.CompleteAsync(claimed.WorkItemId, worker, /*lang=json,strict*/ """{"summary":"done"}""", clock.GetUtcNow(), cancellationToken);
        completed.ShouldBeTrue();

        var beforeDispatch = await LoadOutboxMessagesAsync();
        var pending = beforeDispatch.ShouldHaveSingleItem();
        pending.Type.ShouldBe(RunEventTypes.RunTerminatedV1);
        pending.IsDispatched.ShouldBeFalse();

        // "Process restart": a fresh scope resolves a fresh OutboxDispatcher and
        // runs one sweep — exercising the real FinalizeAsync-enqueued row (not a
        // synthetically seeded one, which OutboxDispatchShould.cs already covers).
        using var freshScope = CreateScope();
        var dispatcher = freshScope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        var (dispatched, deadLettered) = await dispatcher.DispatchAsync(cancellationToken);
        dispatched.ShouldBe(1);
        deadLettered.ShouldBe(0);

        var afterDispatch = await LoadOutboxMessagesAsync();
        afterDispatch.ShouldHaveSingleItem().IsDispatched.ShouldBeTrue();
    }
}
