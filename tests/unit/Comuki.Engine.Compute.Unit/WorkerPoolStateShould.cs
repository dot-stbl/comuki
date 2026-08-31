using Comuki.Engine.Compute.Pool;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="WorkerPoolState"/>: registration and listing per
/// project, busy/idle/touch marks against the fake clock, and the reconcile
/// pass against a substituted <see cref="IComputeProvider"/>.
/// </summary>
public sealed class WorkerPoolStateShould
{
    private readonly FakeTimeProvider clock = new();
    private readonly IComputeProvider computeProvider = Substitute.For<IComputeProvider>();

    private WorkerPoolState CreateState()
    {
        return new(computeProvider, clock);
    }

    private static WorkerHandle CreateHandle(ProjectId projectId)
    {
        return new(WorkerId.New(), $"container-{projectId.Value:N}");
    }

    [Fact]
    public void ListOnlyWorkersOfTheProject()
    {
        var firstProject = ProjectId.New();
        var secondProject = ProjectId.New();
        var state = CreateState();
        var firstHandle = CreateHandle(firstProject);
        state.Register(firstHandle, firstHandle.Id, firstProject, "implement");
        state.Register(CreateHandle(secondProject), WorkerId.New(), secondProject, "docs");

        var workers = state.List(firstProject);

        var worker = workers.ShouldHaveSingleItem();
        worker.Id.ShouldBe(firstHandle.Id);
        worker.ProfileKey.ShouldBe("implement");
        worker.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void MarkBusySetBusyFlagAndRefreshActivity()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");

        clock.Advance(TimeSpan.FromMinutes(5));
        state.MarkBusy(handle.Id);

        var worker = state.List(projectId).ShouldHaveSingleItem();
        worker.IsBusy.ShouldBeTrue();
        worker.LastActiveAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void MarkIdleClearBusyFlagAndRefreshActivity()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");
        state.MarkBusy(handle.Id);

        clock.Advance(TimeSpan.FromMinutes(1));
        state.MarkIdle(handle.Id);

        var worker = state.List(projectId).ShouldHaveSingleItem();
        worker.IsBusy.ShouldBeFalse();
        worker.LastActiveAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void TouchRefreshActivityWithoutChangingBusyFlag()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");
        state.MarkBusy(handle.Id);

        clock.Advance(TimeSpan.FromMinutes(1));
        state.Touch(handle.Id);

        var worker = state.List(projectId).ShouldHaveSingleItem();
        worker.IsBusy.ShouldBeTrue();
        worker.LastActiveAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void IgnoreMarksOfUnknownWorkers()
    {
        var state = CreateState();

        state.MarkBusy(WorkerId.New());
        state.MarkIdle(WorkerId.New());
        state.Touch(WorkerId.New());
    }

    [Fact]
    public void RemoveDropsTheWorkerFromListing()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");

        state.Remove(handle.Id);

        state.List(projectId).ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncAdoptUnknownProviderWorkersAsIdleAsync()
    {
        var projectId = ProjectId.New();
        var workerId = WorkerId.New();
        var state = CreateState();
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = computeProvider.ListAsync(projectId, cancellationToken)
            .Returns([new WorkerInfo(workerId, "container-1", "implement", "img:1", "refs/tags/v1")]);

        await state.SyncFromProviderAsync(projectId, cancellationToken);

        var worker = state.List(projectId).ShouldHaveSingleItem();
        worker.Id.ShouldBe(workerId);
        worker.TokenId.ShouldBe(workerId);
        worker.ProviderRef.ShouldBe("container-1");
        worker.IsBusy.ShouldBeFalse();
        worker.LastActiveAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public async Task SyncDropCachedWorkersTheProviderNoLongerListsAsync()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = computeProvider.ListAsync(projectId, cancellationToken).Returns([]);

        await state.SyncFromProviderAsync(projectId, cancellationToken);

        state.List(projectId).ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncKeepKnownWorkersStateUntouchedAsync()
    {
        var projectId = ProjectId.New();
        var state = CreateState();
        var handle = CreateHandle(projectId);
        state.Register(handle, handle.Id, projectId, "implement");
        state.MarkBusy(handle.Id);
        var registeredAt = state.List(projectId).ShouldHaveSingleItem().LastActiveAt;
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = computeProvider.ListAsync(projectId, cancellationToken)
            .Returns([new WorkerInfo(handle.Id, handle.ProviderRef, "implement", "img:1", "refs/tags/v1")]);

        clock.Advance(TimeSpan.FromMinutes(30));
        await state.SyncFromProviderAsync(projectId, cancellationToken);

        var worker = state.List(projectId).ShouldHaveSingleItem();
        worker.IsBusy.ShouldBeTrue();
        worker.LastActiveAt.ShouldBe(registeredAt);
    }
}
