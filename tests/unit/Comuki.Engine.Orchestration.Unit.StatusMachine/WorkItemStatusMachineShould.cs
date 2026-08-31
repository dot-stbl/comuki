using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Full-matrix tests for <see cref="WorkItemStatusMachine"/> and the
/// <see cref="WorkItem"/> aggregate guard. The expected table below is the
/// spec — production <see cref="WorkItemTransitions"/> must match it
/// pair-for-pair. No <c>Stalled</c> member by design: a stall is an event,
/// the reaper moves the item to queued (requeue) or failed (policy).
/// </summary>
public sealed class WorkItemStatusMachineShould
{
    private const string Image = "ghcr.io/comuki/worker@sha256:9f86d0";
    private const string ProfilesRef = "refs/heads/main";

    private static readonly IReadOnlyDictionary<WorkItemStatus, WorkItemStatus[]> expectedTransitions =
        new Dictionary<WorkItemStatus, WorkItemStatus[]>
        {
            [WorkItemStatus.Blocked] = [WorkItemStatus.Queued, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Queued] = [WorkItemStatus.Running, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Running] = [WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled, WorkItemStatus.Queued],
            [WorkItemStatus.Failed] = [WorkItemStatus.Queued],
            [WorkItemStatus.Succeeded] = [],
            [WorkItemStatus.Cancelled] = [],
        };

    public static TheoryData<WorkItemStatus, WorkItemStatus, bool> Matrix
    {
        get
        {
            var data = new TheoryData<WorkItemStatus, WorkItemStatus, bool>();
            foreach (var from in Enum.GetValues<WorkItemStatus>())
            {
                foreach (var to in Enum.GetValues<WorkItemStatus>())
                {
                    data.Add(from, to, expectedTransitions[from].Contains(to));
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "Given work item statuses from/to, when CanTransition is called, then it matches the transition table")]
    [MemberData(nameof(Matrix))]
    public void MatchTransitionTable(WorkItemStatus from, WorkItemStatus to, bool expected)
    {
        var machine = new WorkItemStatusMachine();

        machine.CanTransition(from, to).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a legal work item transition, when EnsureTransition is called, then it does not throw")]
    public void PassLegalTransitionThroughEnsure()
    {
        var machine = new WorkItemStatusMachine();

        machine.EnsureTransition(WorkItemStatus.Running, WorkItemStatus.Queued);
    }

    [Fact(DisplayName = "Given an illegal work item transition, when EnsureTransition is called, then it throws")]
    public void ThrowOnIllegalTransitionThroughEnsure()
    {
        var machine = new WorkItemStatusMachine();

        var exception = Should.Throw<InvalidOperationException>(
            () => machine.EnsureTransition(WorkItemStatus.Cancelled, WorkItemStatus.Running));
        exception.Message.ShouldContain("Cancelled");
        exception.Message.ShouldContain("Running");
    }

    [Fact(DisplayName = "Given a work item status, when AllowedTargets is called, then it matches the expected one-hop targets")]
    public void ReturnAllowedTargets()
    {
        var machine = new WorkItemStatusMachine();

        foreach (var from in Enum.GetValues<WorkItemStatus>())
        {
            machine.AllowedTargets(from).ShouldBe(expectedTransitions[from], ignoreOrder: true);
        }
    }

    [Fact(DisplayName = "Given plan apply, when Create is called without dependencies, then the item starts queued")]
    public void CreateQueuedWorkItem()
    {
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var item = WorkItem.Create(runId, "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"write tests"}""", WorkItemStatus.Queued, now);

        item.RunId.ShouldBe(runId);
        item.Status.ShouldBe(WorkItemStatus.Queued);
        item.ProfileKey.ShouldBe("implement");
        item.Image.ShouldBe(Image);
        item.ProfilesRef.ShouldBe(ProfilesRef);
        item.Attempt.ShouldBe(0);
        item.Id.Version.ShouldBe(7);
        item.LeasedBy.ShouldBeNull();
        item.LeaseUntil.ShouldBeNull();
        item.HeartbeatAt.ShouldBeNull();
    }

    [Fact(DisplayName = "Given plan apply with dependencies, when Create is called blocked, then the item starts blocked")]
    public void CreateBlockedWorkItem()
    {
        var item = WorkItem.Create(RunId.New(), "docs-writer", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"document"}""", WorkItemStatus.Blocked, DateTimeOffset.UtcNow);

        item.Status.ShouldBe(WorkItemStatus.Blocked);
    }

    [Fact(DisplayName = "Given an initial status other than queued/blocked, when Create is called, then it throws")]
    public void RejectInvalidInitialStatus()
    {
        _ = Should.Throw<ArgumentException>(
            static () => WorkItem.Create(RunId.New(), "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Running, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given an empty profile key, image, profiles ref or brief, when Create is called, then it throws")]
    public void RejectEmptyLabelsAndBrief()
    {
        _ = Should.Throw<ArgumentException>(
            static () => WorkItem.Create(RunId.New(), " ", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, DateTimeOffset.UtcNow));
        _ = Should.Throw<ArgumentException>(
            static () => WorkItem.Create(RunId.New(), "implement", "", ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, DateTimeOffset.UtcNow));
        _ = Should.Throw<ArgumentException>(
            static () => WorkItem.Create(RunId.New(), "implement", Image, " ", /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, DateTimeOffset.UtcNow));
        _ = Should.Throw<ArgumentException>(
            static () => WorkItem.Create(RunId.New(), "implement", Image, ProfilesRef, "", WorkItemStatus.Queued, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a running item, when the lease expires and is requeued, then TransitionTo queued succeeds")]
    public void ApplyRequeueTransitionOnAggregate()
    {
        var item = WorkItem.Create(RunId.New(), "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, DateTimeOffset.UtcNow);
        item.TransitionTo(WorkItemStatus.Running, DateTimeOffset.UtcNow);

        item.TransitionTo(WorkItemStatus.Queued, DateTimeOffset.UtcNow);

        item.Status.ShouldBe(WorkItemStatus.Queued);
    }

    [Fact(DisplayName = "Given a terminal item, when TransitionTo is called, then the aggregate throws")]
    public void RejectIllegalTransitionOnAggregate()
    {
        var item = WorkItem.Create(RunId.New(), "implement", Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, DateTimeOffset.UtcNow);
        item.TransitionTo(WorkItemStatus.Running, DateTimeOffset.UtcNow);
        item.TransitionTo(WorkItemStatus.Succeeded, DateTimeOffset.UtcNow);

        _ = Should.Throw<InvalidOperationException>(() => item.TransitionTo(WorkItemStatus.Running, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a self-referencing edge, when WorkItemDependency.Create is called, then it throws")]
    public void RejectSelfDependency()
    {
        var id = Guid.CreateVersion7();

        _ = Should.Throw<ArgumentException>(() => WorkItemDependency.Create(id, id));
    }

    [Fact(DisplayName = "Given a legal edge, when WorkItemDependency.Create is called, then both ends are set")]
    public void CreateDependencyEdge()
    {
        var dependent = Guid.CreateVersion7();
        var prerequisite = Guid.CreateVersion7();

        var dependency = WorkItemDependency.Create(dependent, prerequisite);

        dependency.WorkItemId.ShouldBe(dependent);
        dependency.DependsOnWorkItemId.ShouldBe(prerequisite);
    }
}
