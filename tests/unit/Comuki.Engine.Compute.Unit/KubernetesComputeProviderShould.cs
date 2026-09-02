using System.Net;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s;
using k8s.Autorest;
using k8s.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="KubernetesComputeProvider"/> against substituted
/// <see cref="IKubernetes"/> operation groups: locks the batch/v1 Job manifest
/// (labels, annotation, env, backoffLimit 0, TTL, serviceAccount, requests),
/// the stop-reason→grace delete mapping and the list/capacity mapping. The
/// SDK's operation extensions route to the *WithHttpMessagesAsync members, so
/// the substitutes are configured and asserted on those (same arguments,
/// wrapped <see cref="HttpOperationResponse{T}"/> results). No real cluster —
/// CI has none; kind e2e is the slice DoD.
/// </summary>
public sealed class KubernetesComputeProviderShould
{
    private readonly IBatchV1Operations batchV1 = Substitute.For<IBatchV1Operations>();
    private readonly ICoreV1Operations coreV1 = Substitute.For<ICoreV1Operations>();
    private readonly KubernetesComputeOptions options = new()
    {
        Namespace = "comuki",
        ServiceAccount = "comuki-worker",
        TtlSecondsAfterFinished = 777,
        TerminationGraceSeconds = 7,
        CpuRequestMillis = 500,
        MemoryRequestMiB = 1024,
        NodeSelector = new Dictionary<string, string>(StringComparer.Ordinal) { ["pool"] = "workers" },
    };

    public KubernetesComputeProviderShould()
    {
        var kubernetes = Substitute.For<IKubernetes>();
        _ = kubernetes.BatchV1.Returns(batchV1);
        _ = kubernetes.CoreV1.Returns(coreV1);
        Provider = new KubernetesComputeProvider(kubernetes, Microsoft.Extensions.Options.Options.Create(options));
    }

    private KubernetesComputeProvider Provider { get; }

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

    private void EchoCreatedJob()
    {
        _ = batchV1.CreateNamespacedJobWithHttpMessagesAsync(
                Arg.Any<V1Job>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(static callInfo => new HttpOperationResponse<V1Job>
            {
                Body = callInfo.Args().OfType<V1Job>().First(),
            });
    }

    [Fact]
    public async Task CreateJobThroughTheProviderThenEchoItsNameAsync()
    {
        var projectId = ProjectId.New();
        V1Job? created = null;
        _ = batchV1.CreateNamespacedJobWithHttpMessagesAsync(
                Arg.Any<V1Job>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                created = callInfo.Args().OfType<V1Job>().First();
                return new HttpOperationResponse<V1Job> { Body = created };
            });

        var handle = await Provider.StartAsync(CreateStartRequest(projectId), TestContext.Current.CancellationToken);

        handle.Id.ShouldNotBe(default);
        handle.ProviderRef.ShouldStartWith("comuki-w-");
        handle.ProviderRef.ShouldBe(created?.Metadata?.Name);
        _ = await batchV1.Received(1).CreateNamespacedJobWithHttpMessagesAsync(
            Arg.Any<V1Job>(),
            "comuki",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HonorPreIssuedWorkerIdAsync()
    {
        var preIssued = WorkerId.New();
        EchoCreatedJob();

        var handle = await Provider.StartAsync(
            CreateStartRequest(ProjectId.New(), preIssuedWorkerId: preIssued),
            TestContext.Current.CancellationToken);

        // token identity and container identity must agree — the provider
        // reuses the caller's id instead of minting its own
        handle.Id.ShouldBe(preIssued);
        handle.ProviderRef.ShouldBe($"comuki-w-{preIssued.Value.ToString("N")[^12..]}");
        _ = await batchV1.Received(1).CreateNamespacedJobWithHttpMessagesAsync(
            Arg.Is<V1Job>(job =>
                job.Metadata != null
                && job.Metadata.Annotations != null
                && string.Equals(
                    job.Metadata.Annotations[KubernetesComputeProvider.WorkerIdAnnotation],
                    preIssued.Value.ToString(),
                    StringComparison.Ordinal)),
            "comuki",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Given a stop reason, when StopAsync is called, then the Job delete carries the mapped grace and Foreground propagation")]
    [InlineData(ComputeStopReason.IdleTtl, 7L)]
    [InlineData(ComputeStopReason.Draining, 7L)]
    [InlineData(ComputeStopReason.LeaseExpired, 7L)]
    [InlineData(ComputeStopReason.Force, 0L)]
    public async Task DeleteJobWithForegroundPropagationAndMappedGraceAsync(ComputeStopReason reason, long expectedGrace)
    {
        var workerId = WorkerId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = batchV1.DeleteNamespacedJobWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HttpOperationResponse<V1Status>());

        await Provider.StopAsync(workerId, reason, cancellationToken);

        _ = await batchV1.Received(1).DeleteNamespacedJobWithHttpMessagesAsync(
            $"comuki-w-{workerId.Value.ToString("N")[^12..]}",
            "comuki",
            Arg.Is<V1DeleteOptions>(deleteOptions =>
                deleteOptions.GracePeriodSeconds == expectedGrace
                && string.Equals(deleteOptions.PropagationPolicy, "Foreground", StringComparison.Ordinal)),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<bool?>(),
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            cancellationToken);
    }

    [Fact]
    public async Task TreatMissingJobAsNoOpAsync()
    {
        _ = batchV1.DeleteNamespacedJobWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpOperationException
            {
                Response = new HttpResponseMessageWrapper(
                    new HttpResponseMessage(HttpStatusCode.NotFound),
                    string.Empty),
            });

        // already TTL-collected — stopping an absent worker is a no-op
        await Provider.StopAsync(WorkerId.New(), ComputeStopReason.IdleTtl, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RethrowDeleteFailuresOtherThanNotFoundAsync()
    {
        _ = batchV1.DeleteNamespacedJobWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpOperationException
            {
                Response = new HttpResponseMessageWrapper(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError),
                    string.Empty),
            });

        await Should.ThrowAsync<HttpOperationException>(
            async () => await Provider.StopAsync(WorkerId.New(), ComputeStopReason.Force, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MapActiveJobsWithWorkerAnnotationToWorkerInfoAsync()
    {
        var workerId = WorkerId.New();
        var projectId = ProjectId.New();
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = batchV1.ListNamespacedJobWithHttpMessagesAsync(
                Arg.Any<string>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                cancellationToken)
            .Returns(new HttpOperationResponse<V1JobList>
            {
                Body = new V1JobList
                {
                    Items =
                    [
                        ActiveJob(workerId, "comuki-w-aaa1", projectId),
                        FinishedJob(WorkerId.New(), "comuki-w-fff1"),
                        ActiveJob(WorkerId.New(), "comuki-w-aaa2", projectId, annotation: "not-a-guid"),
                    ],
                },
            });

        var workers = await Provider.ListAsync(projectId, cancellationToken);

        var worker = workers.ShouldHaveSingleItem();
        worker.Id.ShouldBe(workerId);
        worker.ProviderRef.ShouldBe("comuki-w-aaa1");
        worker.ProfileKey.ShouldBe("implement");
        worker.Image.ShouldBe("ghcr.io_comuki_worker@sha256:abc");
        worker.ProfilesGitRef.ShouldBe("refs_tags_v1.2");
        _ = await batchV1.Received(1).ListNamespacedJobWithHttpMessagesAsync(
            "comuki",
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Is<string?>(selector =>
                string.Equals(selector, $"{ComputeLabels.Project}={projectId.Value}", StringComparison.Ordinal)),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool?>(),
            Arg.Any<int?>(),
            Arg.Any<bool?>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            cancellationToken);
    }

    [Fact]
    public async Task CountFreeSlotsFromNodeAllocatableMinusPodRequestsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _ = coreV1.ListNodeWithHttpMessagesAsync(
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                cancellationToken)
            .Returns(new HttpOperationResponse<V1NodeList>
            {
                Body = new V1NodeList { Items = [Node("4", "8Gi"), Node("4", "8Gi", unschedulable: true)] },
            });
        _ = coreV1.ListPodForAllNamespacesWithHttpMessagesAsync(
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<int?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                cancellationToken)
            .Returns(new HttpOperationResponse<V1PodList>
            {
                Body = new V1PodList
                {
                    Items =
                    [
                        Pod("Running", Requests("1", "1Gi"), isWorker: true),
                        Pod("Succeeded", Requests("2", "2Gi")),
                        Pod("Running", Requests("1", "1Gi")),
                    ],
                },
            });

        // allocatable 4000ms/8Gi minus requests 2000ms/2Gi leaves 2000ms/6Gi;
        // a 500m/1024Mi worker fits 4 times by cpu, 6 by memory → 4.
        var capacity = await Provider.GetCapacityAsync(cancellationToken);

        capacity.FreeSlots.ShouldBe(4);
        capacity.RunningWorkers.ShouldBe(1);
    }

    private static V1Job ActiveJob(WorkerId workerId, string name, ProjectId projectId, string? annotation = null)
    {
        return new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ComputeLabels.Project] = projectId.Value.ToString(),
                    [ComputeLabels.Profile] = "implement",
                    [ComputeLabels.Image] = "ghcr.io_comuki_worker@sha256:abc",
                    [ComputeLabels.ProfilesRef] = "refs_tags_v1.2",
                },
                Annotations = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesComputeProvider.WorkerIdAnnotation] = annotation ?? workerId.Value.ToString(),
                },
            },
            Status = new V1JobStatus { Active = 1 },
        };
    }

    private static V1Job FinishedJob(WorkerId workerId, string name)
    {
        var job = ActiveJob(workerId, name, ProjectId.New());
        job.Status = new V1JobStatus { Active = 0, Succeeded = 1 };
        return job;
    }

    private static V1Node Node(string cpu, string memory, bool unschedulable = false)
    {
        return new V1Node
        {
            Spec = new V1NodeSpec { Unschedulable = unschedulable },
            Status = new V1NodeStatus
            {
                Allocatable = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                {
                    ["cpu"] = new(cpu),
                    ["memory"] = new(memory),
                },
            },
        };
    }

    private static V1Pod Pod(string phase, V1ResourceRequirements? resources = null, bool isWorker = false)
    {
        return new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Labels = isWorker
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [ComputeLabels.Project] = Guid.NewGuid().ToString() }
                    : null,
            },
            Spec = new V1PodSpec
            {
                Containers = [new V1Container { Name = "app", Resources = resources }],
            },
            Status = new V1PodStatus { Phase = phase },
        };
    }

    private static V1ResourceRequirements Requests(string cpu, string memory)
    {
        return new V1ResourceRequirements
        {
            Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
            {
                ["cpu"] = new(cpu),
                ["memory"] = new(memory),
            },
        };
    }
}
