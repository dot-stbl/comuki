using System.Text.Json;
using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Leases;
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
public sealed class WorkItemQueueShould : QueueDatabase
{
    private static readonly DateTimeOffset claimAt = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given two queued items, when two workers claim concurrently, then each gets a distinct item")]
    public async Task DispenseDistinctItemsToConcurrentClaimersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = await SeedQueuedItemAsync();
        _ = await SeedQueuedItemAsync();

        using var scopeA = CreateScope();
        using var scopeB = CreateScope();
        var queueA = scopeA.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var claimA = queueA.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        var claimB = queueB.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
        var claimedA = await claimA;
        var claimedB = await claimB;

        _ = claimedA.ShouldNotBeNull();
        _ = claimedB.ShouldNotBeNull();
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

        _ = first.ShouldNotBeNull();
        first.WorkItemId.ShouldBe(seeded.Id);
        second.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an item queued for another profile, when a worker claims with its labels, then it gets nothing")]
    public async Task RefuseClaimOnLabelMismatchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = await SeedQueuedItemAsync(profileKey: "docs-writer");
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
        _ = first.ShouldNotBeNull();

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
        _ = second.ShouldNotBeNull();
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
            _ = claimed.ShouldNotBeNull();
            claimed.Attempt.ShouldBe(round);

            clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31)));
            _ = await reaper.ReapAsync(cancellationToken);
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
        _ = claimed.ShouldNotBeNull();

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
        _ = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

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
        _ = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

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
        _ = claimed.ShouldNotBeNull();

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
        var transition = events.Single(static runEvent => runEvent.OccurredAt == claimAt.AddMinutes(1));
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
        _ = await queue.ClaimAsync(workerId, ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var failed = await queue.FailAsync(seeded.Id, workerId, "OOM killed", claimAt.AddSeconds(30), cancellationToken);

        failed.ShouldBeTrue();
        var item = (await LoadItemAsync(seeded.Id)).ShouldNotBeNull();
        item.Status.ShouldBe(WorkItemStatus.Failed);

        var events = await LoadEventsAsync(seeded.RunId);
        events.ShouldContain(static runEvent => runEvent.Type == "work_item.status_changed" && runEvent.OccurredAt == claimAt.AddSeconds(30));
        var transition = events.Single(static runEvent => runEvent.OccurredAt == claimAt.AddSeconds(30));
        using var payload = JsonDocument.Parse(transition.Payload);
        payload.RootElement.GetProperty("to").GetString().ShouldBe("Failed");
        payload.RootElement.GetProperty("detail").GetString().ShouldBe("OOM killed");
    }

    [Fact(DisplayName = "Given queued items across profiles, when counted, then the optional profile filter applies")]
    public async Task CountQueuedItemsWithProfileFilterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = await SeedQueuedItemAsync("implement");
        _ = await SeedQueuedItemAsync("implement");
        _ = await SeedQueuedItemAsync("docs-writer");
        using var scope = CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();

        var total = await queue.CountQueuedAsync(cancellationToken: cancellationToken);
        var implement = await queue.CountQueuedAsync("implement", cancellationToken);
        var docs = await queue.CountQueuedAsync("docs-writer", cancellationToken);

        total.ShouldBe(3);
        implement.ShouldBe(2);
        docs.ShouldBe(1);

        _ = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);
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

        _ = claimed.ShouldNotBeNull();
        claimed.WorkItemId.ShouldBe(seeded.Id);
        claimed.LeaseUntil.ShouldBe(clock.GetUtcNow().AddMinutes(2));

        _ = await Should.ThrowAsync<ValidationException>(
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
        _ = await queue.ClaimAsync(WorkerId.New(), ImplementLabels, claimAt.AddMinutes(2), claimAt, cancellationToken);

        var timeline = await journal.ReadTimelineAsync(seeded.RunId, page: 1, pageSize: 10, cancellationToken);

        var entry = timeline.ShouldHaveSingleItem();
        entry.Type.ShouldBe("work_item.status_changed");
        entry.RunId.ShouldBe(seeded.RunId);
    }
}
