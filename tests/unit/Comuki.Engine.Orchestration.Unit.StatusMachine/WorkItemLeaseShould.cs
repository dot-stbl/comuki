using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of the <see cref="WorkItem"/> lease mutators:
/// <see cref="WorkItem.AssignLease"/>, <see cref="WorkItem.Heartbeat"/> and
/// <see cref="WorkItem.ReleaseLease"/> — the domain mirror of the guarded SQL
/// the EF queue runs.
/// </summary>
public sealed class WorkItemLeaseShould
{
    private const string Image = "ghcr.io/comuki/worker@sha256:9f86d0";
    private const string ProfilesRef = "refs/heads/main";
    private static readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a queued item, when AssignLease is called, then the item is running, leased and attempt bumps")]
    public void AssignLeaseFromQueued()
    {
        var workerId = WorkerId.New();
        var leaseUntil = now.AddMinutes(2);
        var item = CreateQueuedItem();

        item.AssignLease(workerId, leaseUntil, now);

        item.Status.ShouldBe(WorkItemStatus.Running);
        item.LeasedBy.ShouldBe(workerId);
        item.LeaseUntil.ShouldBe(leaseUntil);
        item.HeartbeatAt.ShouldBe(now);
        item.Attempt.ShouldBe(1);
    }

    [Theory(DisplayName = "Given a non-queued item, when AssignLease is called, then it throws")]
    [InlineData(WorkItemStatus.Blocked)]
    [InlineData(WorkItemStatus.Running)]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public void RejectAssignLeaseFromNonQueued(WorkItemStatus status)
    {
        var item = CreateItemIn(status);

        var exception = Should.Throw<InvalidOperationException>(() => item.AssignLease(WorkerId.New(), now.AddMinutes(2), now));
        exception.Message.ShouldContain("queued");
    }

    [Fact(DisplayName = "Given a leased running item, when Heartbeat is called, then the lease extends")]
    public void ExtendLeaseThroughHeartbeat()
    {
        var item = ClaimedItem();
        var extendedUntil = now.AddMinutes(4);

        item.Heartbeat(extendedUntil, now.AddMinutes(1));

        item.LeaseUntil.ShouldBe(extendedUntil);
        item.HeartbeatAt.ShouldBe(now.AddMinutes(1));
        item.Status.ShouldBe(WorkItemStatus.Running);
    }

    [Fact(DisplayName = "Given a queued, unleased item, when Heartbeat is called, then it throws")]
    public void RejectHeartbeatOnUnleasedItem()
    {
        var item = CreateQueuedItem();

        var exception = Should.Throw<InvalidOperationException>(() => item.Heartbeat(now.AddMinutes(2), now));
        exception.Message.ShouldContain("running, leased");
    }

    [Fact(DisplayName = "Given a leased running item, when ReleaseLease is called, then the item requeues with a clean lease")]
    public void ReleaseLeaseBackToQueued()
    {
        var item = ClaimedItem();

        item.ReleaseLease(now.AddMinutes(3));

        item.Status.ShouldBe(WorkItemStatus.Queued);
        item.LeasedBy.ShouldBeNull();
        item.LeaseUntil.ShouldBeNull();
        item.HeartbeatAt.ShouldBeNull();
        item.Attempt.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a queued item, when ReleaseLease is called, then it throws")]
    public void RejectReleaseLeaseFromQueued()
    {
        var item = CreateQueuedItem();

        var exception = Should.Throw<InvalidOperationException>(() => item.ReleaseLease(now));
        exception.Message.ShouldContain("running");
    }

    [Fact(DisplayName = "Given a requeued item, when it is claimed again, then the attempt counter accumulates")]
    public void AccumulateAttemptsAcrossClaims()
    {
        var item = ClaimedItem();
        item.ReleaseLease(now.AddMinutes(3));
        item.AssignLease(WorkerId.New(), now.AddMinutes(5), now.AddMinutes(3));

        item.Attempt.ShouldBe(2);
        item.Status.ShouldBe(WorkItemStatus.Running);
    }

    [Fact(DisplayName = "Given a terminal item, when any lease mutator is called, then it throws")]
    public void RejectLeaseMutatorsOnTerminalItem()
    {
        var item = CreateQueuedItem();
        item.TransitionTo(WorkItemStatus.Running, now);
        item.TransitionTo(WorkItemStatus.Succeeded, now.AddMinutes(1));

        _ = Should.Throw<InvalidOperationException>(() => item.AssignLease(WorkerId.New(), now.AddMinutes(2), now));
        _ = Should.Throw<InvalidOperationException>(() => item.Heartbeat(now.AddMinutes(2), now));
        _ = Should.Throw<InvalidOperationException>(() => item.ReleaseLease(now));
    }

    private static WorkItem CreateQueuedItem()
    {
        return CreateItemIn(WorkItemStatus.Queued);
    }

    private static WorkItem ClaimedItem()
    {
        var item = CreateQueuedItem();
        item.AssignLease(WorkerId.New(), now.AddMinutes(2), now);
        return item;
    }

    private static WorkItem CreateItemIn(WorkItemStatus status)
    {
        // Blocked only exists as an initial status (nothing transitions into it);
        // Succeeded needs the two-hop Queued -> Running -> Succeeded path.
        var item = WorkItem.Create(
            RunId.New(),
            "implement",
            Image,
            ProfilesRef,
            /*lang=json,strict*/ """{"goal":"x"}""",
            status == WorkItemStatus.Blocked ? WorkItemStatus.Blocked : WorkItemStatus.Queued,
            now);

        if (status == WorkItemStatus.Succeeded)
        {
            item.TransitionTo(WorkItemStatus.Running, now);
            item.TransitionTo(WorkItemStatus.Succeeded, now);
        }
        else if (status is not (WorkItemStatus.Queued or WorkItemStatus.Blocked))
        {
            item.TransitionTo(status, now);
        }

        return item;
    }
}
