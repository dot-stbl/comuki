using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet;
using Docker.DotNet.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="DockerComputeProvider"/> against a substituted
/// <see cref="IDockerClient"/>: locks the env/label mapping, the stop path
/// and the list/capacity mapping. No real docker.
/// </summary>
public sealed class DockerComputeProviderShould
{
    private readonly IContainerOperations containers = Substitute.For<IContainerOperations>();

    private static ComputeStartRequest CreateStartRequest(ProjectId projectId)
    {
        return new ComputeStartRequest
        {
            ProjectId = projectId,
            ProfileKey = "implement",
            ProfilesGitRef = "refs/tags/v1.2",
            Image = "ghcr.io/comuki/worker@sha256:abc",
            WorkerToken = "secret-token",
            OrchestratorGrpcUrl = new Uri("http://orch:5051"),
            Env = new Dictionary<string, string>(StringComparer.Ordinal) { ["FOO"] = "bar" },
        };
    }

    private DockerComputeProvider CreateProvider(int maxWorkers = 4)
    {
        var docker = Substitute.For<IDockerClient>();
        _ = docker.Containers.Returns(containers);
        var computeOptions = new DockerComputeOptions
        {
            NetworkMode = "comuki-net",
            MaxWorkers = maxWorkers,
            WaitBeforeKillSeconds = 7,
        };
        return new DockerComputeProvider(docker, Microsoft.Extensions.Options.Options.Create(computeOptions));
    }

    private static bool MatchesCreateParameters(
        CreateContainerParameters parameters,
        ComputeStartRequest request,
        WorkerHandle handle,
        ProjectId projectId)
    {
        var grpcUrl = request.OrchestratorGrpcUrl.ToString();
        return string.Equals(parameters.Image, request.Image, StringComparison.Ordinal)
            && parameters.Name.StartsWith($"comuki-{projectId.Value:N}-", StringComparison.Ordinal)
            && parameters.Env.Contains("COMUKI_WORKER_TOKEN=secret-token")
            && parameters.Env.Contains($"COMUKI_PROJECT_ID={projectId.Value}")
            && parameters.Env.Contains("COMUKI_PROFILE_KEY=implement")
            && parameters.Env.Contains("COMUKI_PROFILES_REF=refs/tags/v1.2")
            && parameters.Env.Contains($"COMUKI_ORCH_GRPC={grpcUrl}")
            && parameters.Env.Contains("FOO=bar")
            && parameters.Labels is not null
            && string.Equals(parameters.Labels[ComputeLabels.Project], projectId.Value.ToString(), StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.Profile], "implement", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.Image], "ghcr.io_comuki_worker@sha256:abc", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.ProfilesRef], "refs_tags_v1.2", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[DockerComputeProvider.WorkerIdLabel], handle.Id.Value.ToString(), StringComparison.Ordinal)
            && parameters.HostConfig is not null
            && string.Equals(parameters.HostConfig.NetworkMode, "comuki-net", StringComparison.Ordinal);
    }

    private static bool HasLabelFilter(ContainersListParameters parameters, string labelFilter, bool expectAll)
    {
        return parameters.All == expectAll
            && parameters.Filters is not null
            && parameters.Filters.TryGetValue("label", out var labels)
            && labels.ContainsKey(labelFilter);
    }

    [Fact]
    public async Task CreateContainerWithEnvLabelsAndNetworkThenStartItAsync()
    {
        var projectId = ProjectId.New();
        var request = CreateStartRequest(projectId);
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(new CreateContainerResponse { ID = "container-1" });
        var provider = CreateProvider();

        var handle = await provider.StartAsync(request, cancellationToken);

        handle.ProviderRef.ShouldBe("container-1");
        handle.Id.ShouldNotBe(default);
        _ = await containers.Received(1).CreateContainerAsync(
            Arg.Is<CreateContainerParameters>(parameters => MatchesCreateParameters(parameters, request, handle, projectId)),
            cancellationToken);
        _ = await containers.Received(1).StartContainerAsync(
            "container-1", Arg.Any<ContainerStartParameters>(), cancellationToken);
    }

    [Fact]
    public async Task StopAndRemoveWorkerContainerWithConfiguredGraceAsync()
    {
        var workerId = WorkerId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new() { ID = "container-9" }]);
        var provider = CreateProvider();

        await provider.StopAsync(workerId, ComputeStopReason.Force, cancellationToken);

        _ = await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(parameters =>
                HasLabelFilter(parameters, $"{DockerComputeProvider.WorkerIdLabel}={workerId.Value}", expectAll: true)),
            cancellationToken);
        _ = await containers.Received(1).StopContainerAsync(
            "container-9",
            Arg.Is<ContainerStopParameters>(parameters => parameters.WaitBeforeKillSeconds == 7),
            cancellationToken);
        await containers.Received(1).RemoveContainerAsync(
            "container-9",
            Arg.Is<ContainerRemoveParameters>(parameters => parameters.Force == true),
            cancellationToken);
    }

    [Fact]
    public async Task NotTouchDockerWhenStoppingUnknownWorkerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([]);
        var provider = CreateProvider();

        await provider.StopAsync(WorkerId.New(), ComputeStopReason.IdleTtl, cancellationToken);

        _ = await containers.DidNotReceiveWithAnyArgs().StopContainerAsync(
            default, default, cancellationToken);
        await containers.DidNotReceiveWithAnyArgs().RemoveContainerAsync(
            default, default, cancellationToken);
    }

    [Fact]
    public async Task MapListedContainersToWorkerInfoAsync()
    {
        var projectId = ProjectId.New();
        var workerId = WorkerId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns(
            [
                new()
                {
                    ID = "container-a",
                    Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [DockerComputeProvider.WorkerIdLabel] = workerId.Value.ToString(),
                        [ComputeLabels.Profile] = "implement",
                        [ComputeLabels.Image] = "ghcr.io_comuki_worker@sha256:abc",
                        [ComputeLabels.ProfilesRef] = "refs_tags_v1.2",
                    },
                },
                new()
                {
                    ID = "container-b",
                    Labels = new Dictionary<string, string>(StringComparer.Ordinal),
                },
            ]);
        var provider = CreateProvider();

        var workers = await provider.ListAsync(projectId, cancellationToken);

        var worker = workers.ShouldHaveSingleItem();
        worker.Id.ShouldBe(workerId);
        worker.ProviderRef.ShouldBe("container-a");
        worker.ProfileKey.ShouldBe("implement");
        worker.Image.ShouldBe("ghcr.io_comuki_worker@sha256:abc");
        worker.ProfilesGitRef.ShouldBe("refs_tags_v1.2");
        _ = await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(parameters =>
                HasLabelFilter(parameters, $"{ComputeLabels.Project}={projectId.Value}", expectAll: false)),
            cancellationToken);
    }

    [Fact]
    public async Task CountRunningWorkersAgainstMaxWorkersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new(), new(), new()]);
        var provider = CreateProvider(maxWorkers: 4);

        var capacity = await provider.GetCapacityAsync(cancellationToken);

        capacity.RunningWorkers.ShouldBe(3);
        capacity.FreeSlots.ShouldBe(1);
        _ = await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(static parameters =>
                HasLabelFilter(parameters, ComputeLabels.Project, expectAll: false)),
            cancellationToken);
    }

    [Fact]
    public async Task ClampFreeSlotsToZeroWhenOverCapacityAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new(), new(), new()]);
        var provider = CreateProvider(maxWorkers: 2);

        var capacity = await provider.GetCapacityAsync(cancellationToken);

        capacity.RunningWorkers.ShouldBe(3);
        capacity.FreeSlots.ShouldBe(0);
    }
}
