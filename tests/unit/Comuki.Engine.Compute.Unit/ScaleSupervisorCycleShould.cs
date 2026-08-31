using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Engine.Compute.Settings;
using Comuki.Engine.Compute.Supervisor;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="ScaleSupervisorCycle"/> against fakes: the
/// substituted provider records start/stop calls and mirrors the running
/// pool into List, the backlog reader is scripted per profile. Covers the
/// DoD (3 queued items — 3 starts), the concurrency cap, idle coverage, the
/// drain-then-reap TTL path, the MinIdle floor, busy-worker protection and
/// per-project overrides.
/// </summary>
public sealed class ScaleSupervisorCycleShould
{
    private sealed record StopCall(WorkerId WorkerId, ComputeStopReason Reason);

    private sealed record SupervisorHarness(ScaleSupervisorCycle Cycle, IProjectScaleSettings Settings, WorkerPoolState Pool);

    private readonly ProjectId projectId = ProjectId.New();
    private readonly FakeTimeProvider clock = new();
    private readonly IComputeProvider computeProvider = Substitute.For<IComputeProvider>();
    private readonly IBacklogReader backlogReader = Substitute.For<IBacklogReader>();
    private readonly List<ComputeStartRequest> startedRequests = [];
    private readonly List<WorkerHandle> startedHandles = [];
    private readonly List<StopCall> stoppedWorkers = [];
    private readonly List<WorkerHandle> runningHandles = [];
    private readonly WorkerTokenIssuer tokenIssuer;

    public ScaleSupervisorCycleShould()
    {
        tokenIssuer = new WorkerTokenIssuer(
            clock,
            new InMemoryWorkerTokenStore(),
            Microsoft.Extensions.Options.Options.Create(new WorkerTokenOptions()));

        _ = computeProvider.StartAsync(Arg.Any<ComputeStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<ComputeStartRequest>();
                var handle = new WorkerHandle(WorkerId.New(), $"container-{startedHandles.Count + 1}");
                startedRequests.Add(request);
                startedHandles.Add(handle);
                runningHandles.Add(handle);
                return handle;
            });
        _ = computeProvider.StopAsync(Arg.Any<WorkerId>(), Arg.Any<ComputeStopReason>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var workerId = callInfo.Arg<WorkerId>();
                stoppedWorkers.Add(new StopCall(workerId, callInfo.Arg<ComputeStopReason>()));
                _ = runningHandles.RemoveAll(handle => handle.Id == workerId);
                return Task.CompletedTask;
            });
        _ = computeProvider.ListAsync(Arg.Any<ProjectId>(), Arg.Any<CancellationToken>())
            .Returns(_ => [.. runningHandles.Select(handle => new WorkerInfo(handle.Id, handle.ProviderRef, "implement", "worker:1", "main"))]);
        _ = backlogReader.CountQueuedAsync(Arg.Any<ProjectId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);
    }

    private SupervisorHarness CreateHarness(ScaleSupervisorOptions? options = null)
    {
        options ??= new ScaleSupervisorOptions
        {
            Projects = [projectId.Value],
            ProfileKeys = ["implement"],
        };
        var scaleOptions = Microsoft.Extensions.Options.Options.Create(options);
        var settings = new InMemoryProjectScaleSettings(scaleOptions);
        var pool = new WorkerPoolState(computeProvider, clock);
        var cycle = new ScaleSupervisorCycle(
            scaleOptions,
            backlogReader,
            pool,
            tokenIssuer,
            settings,
            computeProvider,
            clock,
            NullLogger<ScaleSupervisorCycle>.Instance);
        return new SupervisorHarness(cycle, settings, pool);
    }

    private void Queue(int count, string profileKey = "implement")
    {
        _ = backlogReader.CountQueuedAsync(Arg.Any<ProjectId>(), profileKey, Arg.Any<CancellationToken>())
            .Returns(count);
    }

    [Fact]
    public async Task StartOneWorkerPerQueuedItemAsync()
    {
        var harness = CreateHarness();
        Queue(3);

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.Count.ShouldBe(3);
        startedRequests.ShouldAllBe(request => request.ProjectId == projectId && request.ProfileKey == "implement");
        startedRequests.ShouldAllBe(request => request.Image == "ghcr.io/comuki/worker:latest");
        startedRequests.ShouldAllBe(request => request.ProfilesGitRef == "main");
        foreach (var request in startedRequests)
        {
            _ = tokenIssuer.Validate(request.WorkerToken).ShouldNotBeNull();
        }
        stoppedWorkers.ShouldBeEmpty();
        _ = await backlogReader.Received(1).CountQueuedAsync(projectId, "implement", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CapStartsAtProjectMaxConcurrentAsync()
    {
        var harness = CreateHarness(new ScaleSupervisorOptions
        {
            Projects = [projectId.Value],
            ProfileKeys = ["implement"],
            MaxConcurrent = 2,
        });
        Queue(5);

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task NotStartWhenIdlePoolCoversBacklogAsync()
    {
        var harness = CreateHarness();
        Queue(2);
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        Queue(2);
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StopStaleIdleWorkersWithIdleTtlOnDrainAsync()
    {
        var harness = CreateHarness();
        Queue(1);
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        Queue(0);
        clock.Advance(TimeSpan.FromMinutes(11));
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        var stop = stoppedWorkers.ShouldHaveSingleItem();
        stop.Reason.ShouldBe(ComputeStopReason.IdleTtl);
        stop.WorkerId.ShouldBe(startedHandles[0].Id);
        tokenIssuer.Validate(startedRequests[0].WorkerToken).ShouldBeNull();
    }

    [Fact]
    public async Task KeepIdleWorkersBelowMinIdleFloorAsync()
    {
        var harness = CreateHarness(new ScaleSupervisorOptions
        {
            Projects = [projectId.Value],
            ProfileKeys = ["implement"],
            MinIdle = 1,
        });
        Queue(2);
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        Queue(0);
        clock.Advance(TimeSpan.FromMinutes(11));
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        stoppedWorkers.Count.ShouldBe(1);
        harness.Pool.List(projectId).Count.ShouldBe(1);
    }

    [Fact]
    public async Task NotReapBusyWorkersPastIdleTtlAsync()
    {
        var harness = CreateHarness();
        Queue(1);
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);
        harness.Pool.MarkBusy(startedHandles[0].Id);

        Queue(0);
        clock.Advance(TimeSpan.FromMinutes(11));
        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        stoppedWorkers.ShouldBeEmpty();
    }

    [Fact]
    public async Task RespectProjectOverrideForCapImageAndRefAsync()
    {
        var harness = CreateHarness();
        harness.Settings.Override(
            projectId,
            new ProjectScaleSettings(
                MinIdle: 0,
                MaxConcurrent: 1,
                IdleTtl: TimeSpan.FromMinutes(10),
                WorkerImage: "custom/worker@sha256:beef",
                ProfilesGitRef: "refs/tags/v9"));
        Queue(3);

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        var request = startedRequests.ShouldHaveSingleItem();
        request.Image.ShouldBe("custom/worker@sha256:beef");
        request.ProfilesGitRef.ShouldBe("refs/tags/v9");
    }

    [Fact]
    public async Task ScaleProfilesIndependentlyAsync()
    {
        var harness = CreateHarness(new ScaleSupervisorOptions
        {
            Projects = [projectId.Value],
            ProfileKeys = ["implement", "docs"],
        });
        Queue(2, "implement");
        Queue(1, "docs");

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.Count.ShouldBe(3);
        startedRequests.Count(static request => request.ProfileKey == "implement").ShouldBe(2);
        startedRequests.Count(static request => request.ProfileKey == "docs").ShouldBe(1);
    }

    [Fact]
    public async Task ShareMaxConcurrentAcrossProfilesOfTheProjectAsync()
    {
        var harness = CreateHarness(new ScaleSupervisorOptions
        {
            Projects = [projectId.Value],
            ProfileKeys = ["implement", "docs"],
            MaxConcurrent = 2,
        });
        Queue(2, "implement");
        Queue(2, "docs");

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.Count.ShouldBe(2);
        startedRequests.ShouldAllBe(static request => request.ProfileKey == "implement");
    }

    [Fact]
    public async Task DoNothingWhenNoProjectsAreConfiguredAsync()
    {
        var harness = CreateHarness(new ScaleSupervisorOptions());
        Queue(3);

        await harness.Cycle.RunAsync(TestContext.Current.CancellationToken);

        startedRequests.ShouldBeEmpty();
        _ = await backlogReader.DidNotReceive().CountQueuedAsync(
            Arg.Any<ProjectId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
