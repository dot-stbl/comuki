using System.Text.Json;
using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// Claim/lease lifecycle against a real Postgres: the SKIP LOCKED race,
/// owner-guarded heartbeat/complete/fail, the reaper requeue/fail policy and
/// the journal events emitted in the same transactions.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="QueueIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(QueueIntegrationCollection))]
public sealed class WorkItemQueueShould(PostgresCollectionFixture postgres) : QueueDatabase(postgres)
{
    private static readonly DateTimeOffset claimAt = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given two queued items, when two workers claim concurrently, then each gets a distinct item")]
    public async Task DispenseDistinctItemsToConcurrentClaimersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync();
        await SeedQueuedItemAsync();

        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var claimA = queueA.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        var claimB = queueB.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        var claimedA = await claimA;
        var claimedB = await claimB;

        claimedA.ShouldNotBeNull();
        claimedB.ShouldNotBeNull();
        claimedA.WorkItemId.ShouldNotBe(claimedB.WorkItemId);

        var itemA = (await LoadItemAsync(claimedA.WorkItemId)).ShouldNotBeNull();
        var itemB = (await LoadItemAsync(claimedB.WorkItemId)).ShouldNotBeNull();
        itemA.Status.ShouldBe(WorkItemStatus.Running);
        itemB.Status.ShouldBe(WorkItemStatus.Running);
        itemA.Attempt.ShouldBe(1);
        itemB.Attempt.ShouldBe(1);

        // both claims journalled their queued -> running transition
        var events = await LoadEventsAsync(claimedA.RunId);
        events.Count(static runEvent => runEvent.Type == "work_item.status_changed").ShouldBe(1);
    }

    [Fact(DisplayName = "Given a leased item, when another worker claims, then it gets nothing")]
    public async Task RefuseClaimOnLeasedItemAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var first = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        var second = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        first.ShouldNotBeNull();
        first.WorkItemId.ShouldBe(seeded.Id);
        second.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an item queued for another profile, when a worker claims with its labels, then it gets nothing")]
    public async Task RefuseClaimOnLabelMismatchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync(profileKey: "docs-writer");
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var claimed = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        claimed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an expired lease, when the reaper sweeps, then the item requeues, journals the event and is claimable again")]
    public async Task RequeueExpiredLeaseWithJournalEventAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var reaper = scope.ServiceProvider.GetRequiredService<LeaseReaper>();

        var first = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        first.ShouldNotBeNull();

        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31)));
        var reaped = await reaper.ReapAsync(cancellationToken);

        var lease = reaped.ShouldHaveSingleItem();
        lease.WorkItemId.ShouldBe(seeded.Id);
        lease.MarkedFailed.ShouldBeFalse();
        lease.Attempt.ShouldBe(1);

        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Queued);
        item.LeasedBy.ShouldBeNull();
        item.LeaseUntil.ShouldBeNull();
        item.HeartbeatAt.ShouldBeNull();

        var events = await LoadEventsAsync(seeded.RunId);
        events.ShouldContain(static runEvent => runEvent.Type == "work_item.lease_expired");
        var expiry = events.Single(static runEvent => runEvent.Type == "work_item.lease_expired");
        using var payload = JsonDocument.Parse(expiry.Payload);
        payload.RootElement.GetProperty("to").GetString().ShouldBe("Queued");
        payload.RootElement.GetProperty("attempt").GetInt32().ShouldBe(1);

        // and the requeued item is claimable again by another worker, attempt bumps
        var second = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);
        second.ShouldNotBeNull();
        second.WorkItemId.ShouldBe(seeded.Id);
        second.Attempt.ShouldBe(2);
    }

    [Fact(DisplayName = "Given an expired lease at max attempts, when the reaper sweeps, then the item is failed and not claimable")]
    public async Task FailExpiredLeaseAtMaxAttemptsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var reaper = scope.ServiceProvider.GetRequiredService<LeaseReaper>();

        // MaxAttempts=2: first claim expires -> requeue, second claim expires -> failed
        for (var round = 1; round <= 2; round++)
        {
            var claimed = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);
            claimed.ShouldNotBeNull();
            claimed.Attempt.ShouldBe(round);

            clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31)));
            await reaper.ReapAsync(cancellationToken);
        }

        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Failed);
        item.LeasedBy.ShouldBeNull();

        var events = await LoadEventsAsync(seeded.RunId);
        var expiry = events.Last(static runEvent => runEvent.Type == "work_item.lease_expired");
        using var payload = JsonDocument.Parse(expiry.Payload);
        payload.RootElement.GetProperty("to").GetString().ShouldBe("Failed");

        var reclaimed = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);
        reclaimed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a heartbeated lease, when the original expiry passes, then the reaper leaves it alone")]
    public async Task HeartbeatKeepsLeaseFromReaperAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var reaper = scope.ServiceProvider.GetRequiredService<LeaseReaper>();
        var workerId = WorkerId.New();

        var claimed = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        claimed.ShouldNotBeNull();

        clock.Advance(TimeSpan.FromMinutes(1));
        var extended = await queue.HeartbeatAsync(seeded.Id, workerId, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);
        extended.ShouldBeTrue();

        // past the ORIGINAL lease (t0+2m) but within the extended one (t0+3m)
        clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(31)));
        var reaped = await reaper.ReapAsync(cancellationToken);

        reaped.ShouldBeEmpty();
        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Running);
        item.LeasedBy.ShouldBe(workerId);
    }

    [Fact(DisplayName = "Given a lease owned by another worker, when a heartbeat arrives, then it is rejected")]
    public async Task RejectHeartbeatFromNonOwnerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var stranger = await queue.HeartbeatAsync(seeded.Id, WorkerId.New(), claimAt.AddMinutes(4), claimAt.AddSeconds(30), cancellationToken);

        stranger.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a lease that already expired, when the owner heartbeats, then it is rejected")]
    public async Task RejectHeartbeatOnExpiredLeaseAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        clock.Advance(TimeSpan.FromMinutes(3));
        var late = await queue.HeartbeatAsync(seeded.Id, workerId, clock.GetUtcNow().AddMinutes(2), clock.GetUtcNow(), cancellationToken);

        late.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a completed item, when completion is replayed or stolen, then it is refused")]
    public async Task GuardCompletionByLeaseOwnerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        var claimed = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        claimed.ShouldNotBeNull();

        var stolen = await queue.CompleteAsync(seeded.Id, WorkerId.New(), /*lang=json,strict*/ """{"summary":"not mine"}""", claimAt.AddSeconds(30), cancellationToken);
        stolen.ShouldBeFalse();

        var completed = await queue.CompleteAsync(seeded.Id, workerId, /*lang=json,strict*/ """{"summary":"done","filesChanged":2}""", claimAt.AddMinutes(1), cancellationToken);
        completed.ShouldBeTrue();

        var replayed = await queue.CompleteAsync(seeded.Id, workerId, /*lang=json,strict*/ """{"summary":"again"}""", claimAt.AddMinutes(1), cancellationToken);
        replayed.ShouldBeFalse();

        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Succeeded);
        item.LeasedBy.ShouldBeNull();

        var events = await LoadEventsAsync(seeded.RunId);
        events.ShouldContain(static runEvent => runEvent.Type == "work_item.status_changed" && runEvent.OccurredAt == claimAt.AddMinutes(1));
        // The completed item was also the run's last open one, so the same
        // instant carries a sibling run.status_changed — filter by type to
        // pick the work-item transition specifically.
        var transition = events.Single(static runEvent => runEvent.Type == "work_item.status_changed" && runEvent.OccurredAt == claimAt.AddMinutes(1));
        using var payload = JsonDocument.Parse(transition.Payload);
        payload.RootElement.GetProperty("to").GetString().ShouldBe("Succeeded", transition.Payload);
        // the worker result JSON is embedded as the detail value itself
        payload.RootElement.GetProperty("detail").GetProperty("summary").GetString().ShouldBe("done", transition.Payload);
    }

    [Fact(DisplayName = "Given a failed item, when the owner reports the failure, then the reason lands in the journal")]
    public async Task RecordFailureReasonInJournalAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var failed = await queue.FailAsync(seeded.Id, workerId, "OOM killed", claimAt.AddSeconds(30), cancellationToken);

        failed.ShouldBeTrue();
        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Failed);

        var events = await LoadEventsAsync(seeded.RunId);
        events.ShouldContain(static runEvent => runEvent.Type == "work_item.status_changed" && runEvent.OccurredAt == claimAt.AddSeconds(30));
        // The failed item was also the run's last open one, so the same
        // instant carries a sibling run.status_changed — filter by type to
        // pick the work-item transition specifically.
        var transition = events.Single(static runEvent => runEvent.Type == "work_item.status_changed" && runEvent.OccurredAt == claimAt.AddSeconds(30));
        using var payload = JsonDocument.Parse(transition.Payload);
        payload.RootElement.GetProperty("to").GetString().ShouldBe("Failed");
        payload.RootElement.GetProperty("detail").GetString().ShouldBe("OOM killed");
    }

    [Fact(DisplayName = "Given queued items across profiles, when counted, then the optional profile filter applies")]
    public async Task CountQueuedItemsWithProfileFilterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync("implement");
        await SeedQueuedItemAsync("implement");
        await SeedQueuedItemAsync("docs-writer");
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var total = await queue.CountQueuedAsync(cancellationToken: cancellationToken);
        var implement = await queue.CountQueuedAsync("implement", cancellationToken);
        var docs = await queue.CountQueuedAsync("docs-writer", cancellationToken);

        total.ShouldBe(3);
        implement.ShouldBe(2);
        docs.ShouldBe(1);

        await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        (await queue.CountQueuedAsync(cancellationToken: cancellationToken)).ShouldBe(2);
        (await queue.CountQueuedAsync("implement", cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Given the full DI chain, when the claim handler runs, then validation and the configured ttl apply")]
    public async Task ClaimThroughHandlerWithConfiguredTtlAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ClaimWorkItemHandler>();
        var workerId = WorkerId.New();

        var claimed = await handler.HandleAsync(new ClaimWorkItemCommand(workerId, ImplementLabels), cancellationToken);

        claimed.ShouldNotBeNull();
        claimed.WorkItemId.ShouldBe(seeded.Id);
        claimed.LeaseUntil.ShouldBe(clock.GetUtcNow().AddMinutes(2));

        await Should.ThrowAsync<ValidationException>(
            () => handler.HandleAsync(new ClaimWorkItemCommand(workerId, new WorkItemLabels("", ProfilesRef, "implement")), cancellationToken));
    }

    [Fact(DisplayName = "Given a claimed item, when the journal is read through the port, then the claim transition is on the timeline")]
    public async Task ReadClaimTransitionThroughJournalPortAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedQueuedItemAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var timeline = await journal.ReadTimelineAsync(seeded.RunId, page: 1, pageSize: 10, cancellationToken);

        // The claim is also the run's first activation, so the timeline
        // carries a sibling run.status_changed at the same instant — filter
        // by type to isolate the work-item transition under test.
        var entry = timeline.Single(static runEvent => runEvent.Type == "work_item.status_changed");
        entry.RunId.ShouldBe(seeded.RunId);
    }

    /// <summary>Seeds a run with one Queued prerequisite and one Blocked
    /// dependent (single edge), for the dependency-gated claim tests.</summary>
    private async Task<(WorkItem Prerequisite, WorkItem Dependent)> SeedBlockedDependentAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = clock.GetUtcNow();
        var run = Run.Create(ProjectId.New(), now);
        var prerequisite = WorkItem.Create(run.Id, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"prerequisite"}""", WorkItemStatus.Queued, now);
        var dependent = WorkItem.Create(run.Id, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"dependent"}""", WorkItemStatus.Blocked, now);
        db.Runs.Add(run);
        db.WorkItems.AddRange(prerequisite, dependent);
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependent.Id, prerequisite.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (prerequisite, dependent);
    }

    [Fact(DisplayName = "Given a blocked item whose prerequisite has not succeeded, when claimed, then it is never returned")]
    public async Task RefuseClaimOnBlockedDependentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (prerequisite, dependent) = await SeedBlockedDependentAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        // Claim the prerequisite first so it is Running (leased), not Queued —
        // isolates that the Blocked dependent specifically is excluded by
        // status alone, not merely out-competed for the same row.
        var first = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        first.ShouldNotBeNull();
        first.WorkItemId.ShouldBe(prerequisite.Id);

        var second = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        second.ShouldBeNull();
        var item = (await LoadItemAsync(dependent.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Blocked);
    }

    [Fact(DisplayName = "Given a dependent's only prerequisite succeeds, when completed, then the dependent unblocks in the same transaction and is claimable")]
    public async Task UnblockDependentOnPrerequisiteSuccessAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (prerequisite, dependent) = await SeedBlockedDependentAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        var claimed = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        claimed.ShouldNotBeNull();
        claimed.WorkItemId.ShouldBe(prerequisite.Id);

        var completed = await queue.CompleteAsync(prerequisite.Id, workerId, /*lang=json,strict*/ """{"summary":"done"}""", claimAt.AddMinutes(1), cancellationToken);

        completed.ShouldBeTrue();
        var unblocked = (await LoadItemAsync(dependent.Id)).ShouldNotBeNull();
        unblocked.Status.ShouldBe(WorkItemStatus.Queued);

        var second = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(3), claimAt.AddMinutes(1), cancellationToken);
        second.ShouldNotBeNull();
        second.WorkItemId.ShouldBe(dependent.Id);
    }

    [Fact(DisplayName = "Given a dependent's prerequisite fails, when failed, then the dependent stays blocked and is never claimed")]
    public async Task PrerequisiteFailureDoesNotUnblockDependentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (prerequisite, dependent) = await SeedBlockedDependentAsync();
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerId = WorkerId.New();
        await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var failed = await queue.FailAsync(prerequisite.Id, workerId, "boom", claimAt.AddMinutes(1), cancellationToken);

        failed.ShouldBeTrue();
        var stillBlocked = (await LoadItemAsync(dependent.Id)).ShouldNotBeNull();
        stillBlocked.Status.ShouldBe(WorkItemStatus.Blocked);

        var claimed = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(3), claimAt.AddMinutes(1), cancellationToken);
        claimed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a dependent with two prerequisites, when only one succeeds, then it stays blocked until the last one also succeeds")]
    public async Task UnblockOnlyAfterEveryPrerequisiteSucceedsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = clock.GetUtcNow();
        var run = Run.Create(ProjectId.New(), now);
        var prerequisiteA = WorkItem.Create(run.Id, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"a"}""", WorkItemStatus.Queued, now);
        var prerequisiteB = WorkItem.Create(run.Id, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"b"}""", WorkItemStatus.Queued, now);
        var dependent = WorkItem.Create(run.Id, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"c"}""", WorkItemStatus.Blocked, now);
        db.Runs.Add(run);
        db.WorkItems.AddRange(prerequisiteA, prerequisiteB, dependent);
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependent.Id, prerequisiteA.Id));
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependent.Id, prerequisiteB.Id));
        await db.SaveChangesAsync(cancellationToken);

        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var workerA = WorkerId.New();
        var claimedA = await queue.ClaimAsync(workerA, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        claimedA.ShouldNotBeNull();
        await queue.CompleteAsync(prerequisiteA.Id, workerA, /*lang=json,strict*/ """{"summary":"a done"}""", claimAt.AddMinutes(1), cancellationToken);

        // Only A succeeded so far — B is still Queued, dependent must stay Blocked.
        (await LoadItemAsync(dependent.Id)).ShouldNotBeNull().Status.ShouldBe(WorkItemStatus.Blocked);

        var workerB = WorkerId.New();
        var claimedB = await queue.ClaimAsync(workerB, ImplementLabels, claimAt.AddMinutes(3), claimAt.AddMinutes(1), cancellationToken);
        claimedB.ShouldNotBeNull();
        claimedB.WorkItemId.ShouldBe(prerequisiteB.Id);
        await queue.CompleteAsync(prerequisiteB.Id, workerB, /*lang=json,strict*/ """{"summary":"b done"}""", claimAt.AddMinutes(2), cancellationToken);

        // Both prerequisites Succeeded now — dependent unblocks.
        (await LoadItemAsync(dependent.Id)).ShouldNotBeNull().Status.ShouldBe(WorkItemStatus.Queued);
    }

    /// <summary>Seeds a run with two prerequisites and TWO dependents that
    /// each depend on BOTH — a genuine two-row overlap between the two
    /// prerequisites' unblock candidate sets, for the deadlock-race test
    /// below (a single shared dependent can't itself deadlock; you need at
    /// least two overlapping rows for a lock-order cycle to be possible).</summary>
    private async Task<(WorkItem PrerequisiteA, WorkItem PrerequisiteB, WorkItem DependentOne, WorkItem DependentTwo)> SeedDiamondDependentsAsync(string profileKey)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = clock.GetUtcNow();
        var run = Run.Create(ProjectId.New(), now);
        var prerequisiteA = WorkItem.Create(run.Id, profileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"a"}""", WorkItemStatus.Queued, now);
        var prerequisiteB = WorkItem.Create(run.Id, profileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"b"}""", WorkItemStatus.Queued, now);
        var dependentOne = WorkItem.Create(run.Id, profileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"diamond-1"}""", WorkItemStatus.Blocked, now);
        var dependentTwo = WorkItem.Create(run.Id, profileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"diamond-2"}""", WorkItemStatus.Blocked, now);
        db.Runs.Add(run);
        db.WorkItems.AddRange(prerequisiteA, prerequisiteB, dependentOne, dependentTwo);
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependentOne.Id, prerequisiteA.Id));
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependentOne.Id, prerequisiteB.Id));
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependentTwo.Id, prerequisiteA.Id));
        db.WorkItemDependencies.Add(WorkItemDependency.Create(dependentTwo.Id, prerequisiteB.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (prerequisiteA, prerequisiteB, dependentOne, dependentTwo);
    }

    [Fact(DisplayName = "Given two dependents sharing both prerequisites, when both prerequisites complete concurrently across many trials, then both unblock exactly once with no deadlock")]
    public async Task UnblockDiamondDependentsWithoutDeadlockUnderConcurrencyAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const int trials = 25;

        for (var trial = 0; trial < trials; trial++)
        {
            var profileKey = $"diamond-{trial}";
            var (prerequisiteA, prerequisiteB, dependentOne, dependentTwo) = await SeedDiamondDependentsAsync(profileKey);
            var labels = new WorkItemLabels(Image, ProfilesRef, profileKey);
            using var scopeA = CreateScope();
            using var scopeB = CreateScope();
            var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            var workerA = WorkerId.New();
            var workerB = WorkerId.New();
            var claimedA = await queueA.ClaimAsync(workerA, labels, claimAt.AddMinutes(2), claimAt, cancellationToken);
            var claimedB = await queueB.ClaimAsync(workerB, labels, claimAt.AddMinutes(2), claimAt, cancellationToken);
            claimedA.ShouldNotBeNull();
            claimedB.ShouldNotBeNull();

            // fire both completions without awaiting either first — genuinely
            // concurrent transactions on separate connections, so a real lock-
            // order mismatch between the two UnblockDependentsSql candidate
            // sets would surface as an actual Postgres 40P01 (uncaught — this
            // fact fails loudly on it), not a theoretical one
            var completeA = queueA.CompleteAsync(claimedA.WorkItemId, workerA, /*lang=json,strict*/ """{"summary":"a done"}""", claimAt.AddMinutes(1), cancellationToken);
            var completeB = queueB.CompleteAsync(claimedB.WorkItemId, workerB, /*lang=json,strict*/ """{"summary":"b done"}""", claimAt.AddMinutes(1), cancellationToken);
            var resultA = await completeA;
            var resultB = await completeB;

            resultA.ShouldBeTrue($"trial {trial}: prerequisite A completion was rejected");
            resultB.ShouldBeTrue($"trial {trial}: prerequisite B completion was rejected");
            (await LoadItemAsync(dependentOne.Id)).ShouldNotBeNull().Status.ShouldBe(WorkItemStatus.Queued, $"trial {trial}: dependent one did not unblock exactly once");
            (await LoadItemAsync(dependentTwo.Id)).ShouldNotBeNull().Status.ShouldBe(WorkItemStatus.Queued, $"trial {trial}: dependent two did not unblock exactly once");
        }
    }
}
