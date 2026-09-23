using System.Net;
using Comuki.Engine.Compute.Exceptions;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="DockerComputeProvider"/> against a substituted
/// <see cref="IContainerOperations"/> (and <see cref="INetworkOperations"/>
/// through the real <see cref="DockerEgressFence"/>): locks the env/label
/// mapping, the fenced-network start path, the fail-closed fence refusals,
/// the stop path and the list/capacity mapping. No real docker.
/// </summary>
public sealed class DockerComputeProviderShould
{
    private const string FencedNetwork = "comuki-worker-net";

    private readonly IContainerOperations containers = Substitute.For<IContainerOperations>();
    private readonly INetworkOperations networks = Substitute.For<INetworkOperations>();

    private static ComputeStartRequest CreateStartRequest(ProjectId projectId, WorkerId? preIssuedWorkerId = null)
    {
        return new ComputeStartRequest
        {
            ProjectId = projectId,
            PreIssuedWorkerId = preIssuedWorkerId,
            ProfileKey = "implement",
            ProfilesGitRef = "refs/tags/v1.2",
            Image = "ghcr.io/comuki/worker@sha256:abc",
            WorkerToken = "secret-token",
            OrchestratorGrpcUrl = new Uri("http://orch:5051"),
            Env = new Dictionary<string, string>(StringComparer.Ordinal) { ["FOO"] = "bar" },
        };
    }

    private DockerComputeProvider CreateProvider(int maxWorkers = 4, bool allowUnfencedEgress = false)
    {
        var computeOptions = new DockerComputeOptions
        {
            NetworkMode = "comuki-net",
            FencedNetwork = FencedNetwork,
            MaxWorkers = maxWorkers,
            WaitBeforeKillSeconds = 7,
        };
        var fence = new DockerEgressFence(networks, NullLogger<DockerEgressFence>.Instance);
        return new DockerComputeProvider(
            fence,
            containers,
            Microsoft.Extensions.Options.Options.Create(new ComputeOptions { AllowUnfencedEgress = allowUnfencedEgress }),
            Microsoft.Extensions.Options.Options.Create(computeOptions));
    }

    private void InspectFenceReturns(bool internalNetwork)
    {
        networks.InspectNetworkAsync(FencedNetwork, Arg.Any<CancellationToken>())
            .Returns(new NetworkResponse { Name = FencedNetwork, Internal = internalNetwork });
    }

    private static bool MatchesCreateParameters(
        CreateContainerParameters parameters,
        ComputeStartRequest request,
        WorkerHandle handle,
        ProjectId projectId)
    {
        var grpcUrl = request.OrchestratorGrpcUrl.ToString();
        return string.Equals(parameters.Image, request.Image, StringComparison.Ordinal)
            && parameters.Name is not null
            && parameters.Name.StartsWith($"comuki-{projectId.Value:N}-", StringComparison.Ordinal)
            && parameters.Env.Contains("COMUKI_WORKER_TOKEN=secret-token")
            && parameters.Env.Contains($"COMUKI_PROJECT_ID={projectId.Value}")
            && parameters.Env.Contains("COMUKI_PROFILE_KEY=implement")
            && parameters.Env.Contains("COMUKI_PROFILES_REF=refs/tags/v1.2")
            && parameters.Env.Contains("COMUKI_WORKER_IMAGE=ghcr.io/comuki/worker@sha256:abc")
            && parameters.Env.Contains($"COMUKI_ORCH_GRPC={grpcUrl}")
            && parameters.Env.Contains("FOO=bar")
            && parameters.Labels is not null
            && string.Equals(parameters.Labels[ComputeLabels.Project], projectId.Value.ToString(), StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.Profile], "implement", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.Image], "ghcr.io_comuki_worker@sha256:abc", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[ComputeLabels.ProfilesRef], "refs_tags_v1.2", StringComparison.Ordinal)
            && string.Equals(parameters.Labels[DockerComputeProvider.WorkerIdLabel], handle.Id.Value.ToString(), StringComparison.Ordinal)
            && parameters.HostConfig is not null
            && string.Equals(parameters.HostConfig.NetworkMode, FencedNetwork, StringComparison.Ordinal);
    }

    private static bool HasLabelFilter(ContainersListParameters parameters, string labelFilter, bool expectAll)
    {
        return parameters.All == expectAll
            && parameters.Filters is not null
            && parameters.Filters.TryGetValue("label", out var labels)
            && labels.ContainsKey(labelFilter);
    }

    [Fact(DisplayName = "When create Container With Env Labels And Fenced Network Then Start It Async, then test passes")]
    public async Task CreateContainerWithEnvLabelsAndFencedNetworkThenStartItAsync()
    {
        var projectId = ProjectId.New();
        var request = CreateStartRequest(projectId);
        var cancellationToken = TestContext.Current.CancellationToken;
        InspectFenceReturns(internalNetwork: true);
        containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(new CreateContainerResponse { ID = "container-1" });
        var provider = CreateProvider();

        var handle = await provider.StartAsync(request, cancellationToken);

        handle.ProviderRef.ShouldBe("container-1");
        handle.Id.ShouldNotBe(default);
        await networks.Received(1).InspectNetworkAsync(FencedNetwork, cancellationToken);
        await containers.Received(1).CreateContainerAsync(
            Arg.Is<CreateContainerParameters>(parameters => MatchesCreateParameters(parameters, request, handle, projectId)),
            cancellationToken);
        await containers.Received(1).StartContainerAsync(
            "container-1", Arg.Any<ContainerStartParameters>(), cancellationToken);
    }

    [Fact(DisplayName = "Given the fenced network is missing, when the unfenced flag is false, then start is refused and no container is created")]
    public async Task RefuseStartWhenFencedNetworkIsMissingAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        networks.InspectNetworkAsync(FencedNetwork, Arg.Any<CancellationToken>())
            .Throws(new DockerApiException(HttpStatusCode.NotFound, "no such network"));
        var provider = CreateProvider();

        var exception = await Should.ThrowAsync<ComputeFenceException>(
            async () => await provider.StartAsync(CreateStartRequest(ProjectId.New()), cancellationToken));

        exception.Message.ShouldContain(FencedNetwork);
        await containers.DidNotReceiveWithAnyArgs().CreateContainerAsync(
            Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>());
        await containers.DidNotReceiveWithAnyArgs().StartContainerAsync(
            string.Empty, Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the fenced network exists but is not internal, when the unfenced flag is false, then start is refused")]
    public async Task RefuseStartWhenFencedNetworkIsNotInternalAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        InspectFenceReturns(internalNetwork: false);
        var provider = CreateProvider();

        var exception = await Should.ThrowAsync<ComputeFenceException>(
            async () => await provider.StartAsync(CreateStartRequest(ProjectId.New()), cancellationToken));

        exception.Message.ShouldContain("not internal");
        await containers.DidNotReceiveWithAnyArgs().CreateContainerAsync(
            Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no fenced network is configured, when the unfenced flag is false, then start is refused")]
    public async Task RefuseStartWhenNoFencedNetworkIsConfiguredAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var computeOptions = new DockerComputeOptions { NetworkMode = "bridge" };
        var provider = new DockerComputeProvider(
            new DockerEgressFence(networks, NullLogger<DockerEgressFence>.Instance),
            containers,
            Microsoft.Extensions.Options.Options.Create(new ComputeOptions()),
            Microsoft.Extensions.Options.Options.Create(computeOptions));

        await Should.ThrowAsync<ComputeFenceException>(
            async () => await provider.StartAsync(CreateStartRequest(ProjectId.New()), cancellationToken));

        await networks.DidNotReceiveWithAnyArgs().InspectNetworkAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await containers.DidNotReceiveWithAnyArgs().CreateContainerAsync(
            Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the fenced network is missing, when the unfenced flag is true, then start proceeds on the fallback network")]
    public async Task StartUnfencedOnFallbackNetworkWhenOverrideIsSetAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        networks.InspectNetworkAsync(FencedNetwork, Arg.Any<CancellationToken>())
            .Throws(new DockerApiException(HttpStatusCode.NotFound, "no such network"));
        containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(new CreateContainerResponse { ID = "container-unfenced" });
        var provider = CreateProvider(allowUnfencedEgress: true);

        var handle = await provider.StartAsync(CreateStartRequest(ProjectId.New()), cancellationToken);

        handle.ProviderRef.ShouldBe("container-unfenced");
        await containers.Received(1).CreateContainerAsync(
            Arg.Is<CreateContainerParameters>(static parameters =>
                parameters.HostConfig != null
                && string.Equals(parameters.HostConfig.NetworkMode, "comuki-net", StringComparison.Ordinal)),
            cancellationToken);
    }

    [Fact(DisplayName = "Given hardening defaults, when creating the container, then limits, cap drop, no-new-privileges and non-root user are set")]
    public async Task StampLimitsAndHardeningOnWorkerContainerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        InspectFenceReturns(internalNetwork: true);
        CreateContainerParameters? captured = null;
        containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<CreateContainerParameters>();
                return new CreateContainerResponse { ID = "container-hardened" };
            });
        var provider = CreateProvider();

        await provider.StartAsync(CreateStartRequest(ProjectId.New()), cancellationToken);

        var parameters = captured.ShouldNotBeNull();
        parameters.User.ShouldBe("1000");
        var hostConfig = parameters.HostConfig.ShouldNotBeNull();
        hostConfig.Memory.ShouldBe(2L * 1024 * 1024 * 1024);
        hostConfig.NanoCPUs.ShouldBe(1_000_000_000L);
        hostConfig.CapDrop.ShouldNotBeNull().ShouldContain(DockerComputeMapping.DropAllCapabilities);
        hostConfig.SecurityOpt.ShouldNotBeNull().ShouldContain(DockerComputeMapping.NoNewPrivileges);

        // no bind mounts at all — the docker socket never enters a worker.
        hostConfig.Binds.ShouldBeNull();
    }

    [Fact(DisplayName = "When honor Pre Issued Worker Id When Provided Async, then test passes")]
    public async Task HonorPreIssuedWorkerIdWhenProvidedAsync()
    {
        var projectId = ProjectId.New();
        var preIssued = WorkerId.New();
        var request = CreateStartRequest(projectId, preIssuedWorkerId: preIssued);
        var cancellationToken = TestContext.Current.CancellationToken;
        InspectFenceReturns(internalNetwork: true);
        containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(new CreateContainerResponse { ID = "container-pre" });
        var provider = CreateProvider();

        var handle = await provider.StartAsync(request, cancellationToken);

        // the token identity and the container identity must agree —
        // the provider reuses the caller's id instead of minting its own
        handle.Id.ShouldBe(preIssued);
        var expectedNameSuffix = preIssued.Value.ToString("N")[..12];
        var expectedWorkerIdLabel = preIssued.Value.ToString();
        await containers.Received(1).CreateContainerAsync(
            Arg.Is<CreateContainerParameters>(parameters =>
                parameters.Labels != null
                && string.Equals(
                    parameters.Labels[DockerComputeProvider.WorkerIdLabel],
                    expectedWorkerIdLabel,
                    StringComparison.Ordinal)
                && parameters.Name != null
                && parameters.Name.EndsWith(expectedNameSuffix, StringComparison.Ordinal)),
            cancellationToken);
    }

    [Fact(DisplayName = "When mint Fresh Worker Id When None Pre Issued Async, then test passes")]
    public async Task MintFreshWorkerIdWhenNonePreIssuedAsync()
    {
        var projectId = ProjectId.New();
        var request = CreateStartRequest(projectId);
        var cancellationToken = TestContext.Current.CancellationToken;
        InspectFenceReturns(internalNetwork: true);
        containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), cancellationToken)
            .Returns(new CreateContainerResponse { ID = "container-mint" });
        var provider = CreateProvider();

        var handle = await provider.StartAsync(request, cancellationToken);

        handle.Id.ShouldNotBe(default);
        request.PreIssuedWorkerId.ShouldBeNull();
    }

    [Fact(DisplayName = "When stop And Remove Worker Container With Configured Grace Async, then test passes")]
    public async Task StopAndRemoveWorkerContainerWithConfiguredGraceAsync()
    {
        var workerId = WorkerId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new() { ID = "container-9" }]);
        var provider = CreateProvider();

        await provider.StopAsync(workerId, ComputeStopReason.Force, cancellationToken);

        await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(parameters =>
                HasLabelFilter(parameters, $"{DockerComputeProvider.WorkerIdLabel}={workerId.Value}", expectAll: true)),
            cancellationToken);
        await containers.Received(1).StopContainerAsync(
            "container-9",
            Arg.Is<ContainerStopParameters>(parameters => parameters.WaitBeforeKillSeconds == 7),
            cancellationToken);
        await containers.Received(1).RemoveContainerAsync(
            "container-9",
            Arg.Is<ContainerRemoveParameters>(parameters => parameters.Force == true),
            cancellationToken);
    }

    [Fact(DisplayName = "When not Touch Docker When Stopping Unknown Worker Async, then test passes")]
    public async Task NotTouchDockerWhenStoppingUnknownWorkerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([]);
        var provider = CreateProvider();

        await provider.StopAsync(WorkerId.New(), ComputeStopReason.IdleTtl, cancellationToken);

        await containers.DidNotReceiveWithAnyArgs().StopContainerAsync(
            string.Empty, new ContainerStopParameters(), cancellationToken);
        await containers.DidNotReceiveWithAnyArgs().RemoveContainerAsync(
            string.Empty, new ContainerRemoveParameters(), cancellationToken);
    }

    [Fact(DisplayName = "When map Listed Containers To Worker Info Async, then test passes")]
    public async Task MapListedContainersToWorkerInfoAsync()
    {
        var projectId = ProjectId.New();
        var workerId = WorkerId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
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
        await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(parameters =>
                HasLabelFilter(parameters, $"{ComputeLabels.Project}={projectId.Value}", expectAll: false)),
            cancellationToken);
    }

    [Fact(DisplayName = "When count Running Workers Against Max Workers Async, then test passes")]
    public async Task CountRunningWorkersAgainstMaxWorkersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new(), new(), new()]);
        var provider = CreateProvider(maxWorkers: 4);

        var capacity = await provider.GetCapacityAsync(cancellationToken);

        capacity.RunningWorkers.ShouldBe(3);
        capacity.FreeSlots.ShouldBe(1);
        await containers.Received(1).ListContainersAsync(
            Arg.Is<ContainersListParameters>(static parameters =>
                HasLabelFilter(parameters, ComputeLabels.Project, expectAll: false)),
            cancellationToken);
    }

    [Fact(DisplayName = "When clamp Free Slots To Zero When Over Capacity Async, then test passes")]
    public async Task ClampFreeSlotsToZeroWhenOverCapacityAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), cancellationToken)
            .Returns([new(), new(), new()]);
        var provider = CreateProvider(maxWorkers: 2);

        var capacity = await provider.GetCapacityAsync(cancellationToken);

        capacity.RunningWorkers.ShouldBe(3);
        capacity.FreeSlots.ShouldBe(0);
    }
}
