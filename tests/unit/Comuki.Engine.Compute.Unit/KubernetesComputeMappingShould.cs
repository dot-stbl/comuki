using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s.Models;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Granular assertions on the pure <see cref="KubernetesComputeMapping"/>:
/// the batch/v1 Job manifest a worker start produces (labels, annotation,
/// env contract, backoffLimit 0, TTL, serviceAccount, nodeSelector,
/// resource requests) and the stop-reason delete mapping.
/// </summary>
public sealed class KubernetesComputeMappingShould
{
    private readonly KubernetesComputeOptions options = new()
    {
        Namespace = "comuki",
        ServiceAccount = "comuki-worker",
        TtlSecondsAfterFinished = 300,
        TerminationGraceSeconds = 12,
        CpuRequestMillis = 250,
        MemoryRequestMiB = 512,
        NodeSelector = new Dictionary<string, string>(StringComparer.Ordinal) { ["pool"] = "workers" },
    };

    [Fact]
    public void BuildJobWithLabelsAnnotationEnvAndPolicy()
    {
        var projectId = ProjectId.New();
        var workerId = WorkerId.New();
        var request = CreateStartRequest(projectId);

        var job = KubernetesComputeMapping.ToJob(request, workerId, options);

        job.Metadata?.Name.ShouldBe($"comuki-w-{workerId.Value.ToString("N")[^12..]}");
        var metadata = job.Metadata.ShouldNotBeNull();
        var labels = metadata.Labels.ShouldNotBeNull();
        labels[ComputeLabels.Project].ShouldBe(projectId.Value.ToString());
        labels[ComputeLabels.Profile].ShouldBe("implement");
        labels[ComputeLabels.Image].ShouldBe("ghcr.io_comuki_worker@sha256:abc");
        labels[ComputeLabels.ProfilesRef].ShouldBe("refs_tags_v1.2");
        metadata.Annotations.ShouldNotBeNull()[KubernetesComputeProvider.WorkerIdAnnotation]
            .ShouldBe(workerId.Value.ToString());

        var spec = job.Spec.ShouldNotBeNull();
        spec.BackoffLimit.ShouldBe(0);
        spec.TtlSecondsAfterFinished.ShouldBe(300);

        var template = spec.Template.ShouldNotBeNull();
        var templateLabels = template.Metadata.ShouldNotBeNull().Labels.ShouldNotBeNull();
        templateLabels.Count.ShouldBe(labels.Count);
        foreach (var pair in labels)
        {
            templateLabels.ContainsKey(pair.Key).ShouldBeTrue();
        }

        var podSpec = template.Spec.ShouldNotBeNull();
        podSpec.ServiceAccountName.ShouldBe("comuki-worker");
        podSpec.NodeSelector.ShouldNotBeNull()["pool"].ShouldBe("workers");
        podSpec.RestartPolicy.ShouldBe("Never");

        var container = podSpec.Containers.ShouldHaveSingleItem();
        container.Name.ShouldBe("worker");
        container.Image.ShouldBe("ghcr.io/comuki/worker@sha256:abc");
        var environment = container.Env.ShouldNotBeNull();
        environment.ShouldContain(env => env.Name == "COMUKI_WORKER_TOKEN" && env.Value == "secret-token");
        environment.ShouldContain(env => env.Name == "COMUKI_PROJECT_ID" && env.Value == projectId.Value.ToString());
        environment.ShouldContain(env => env.Name == "COMUKI_PROFILE_KEY" && env.Value == "implement");
        environment.ShouldContain(env => env.Name == "COMUKI_PROFILES_REF" && env.Value == "refs/tags/v1.2");
        environment.ShouldContain(env => env.Name == "COMUKI_WORKER_IMAGE" && env.Value == "ghcr.io/comuki/worker@sha256:abc");
        environment.ShouldContain(env => env.Name == "COMUKI_ORCH_GRPC" && env.Value == request.OrchestratorGrpcUrl.ToString());
        // caller extras come after the contract env
        environment.Last().Name.ShouldBe("FOO");
        environment.Last().Value.ShouldBe("bar");

        var resources = container.Resources.ShouldNotBeNull();
        var requests = resources.Requests.ShouldNotBeNull();
        requests["cpu"].ToString().ShouldBe("250m");
        // quantity canonicalizes freely (512Mi ⇄ 0.5Gi) — compare parsed bytes
        KubernetesCapacityMath.ParseMemoryBytes(requests["memory"].ToString()).ShouldBe(512L * 1024 * 1024);
    }

    [Fact]
    public void OmitNodeSelectorWhenNotConfigured()
    {
        var bareOptions = new KubernetesComputeOptions { Namespace = "comuki" };

        var job = KubernetesComputeMapping.ToJob(CreateStartRequest(ProjectId.New()), WorkerId.New(), bareOptions);

        job.Spec?.Template?.Spec?.NodeSelector.ShouldBeNull();
    }

    [Fact]
    public void DeriveJobNameFromWorkerIdAlone()
    {
        var workerId = WorkerId.New();

        var name = KubernetesComputeMapping.ToJobName(workerId);

        name.ShouldBe($"comuki-w-{workerId.Value.ToString("N")[^12..]}");
        name.ShouldNotBe(KubernetesComputeMapping.ToJobName(WorkerId.New()));
    }

    [Fact]
    public void BuildProjectLabelSelector()
    {
        var projectId = ProjectId.New();

        KubernetesComputeMapping.ToProjectLabelSelector(projectId)
            .ShouldBe($"{ComputeLabels.Project}={projectId.Value}");
    }

    [Theory(DisplayName = "Given a stop reason, when delete options are mapped, then grace is configured for soft reasons and zero for Force")]
    [InlineData(ComputeStopReason.IdleTtl, 12L)]
    [InlineData(ComputeStopReason.Draining, 12L)]
    [InlineData(ComputeStopReason.LeaseExpired, 12L)]
    [InlineData(ComputeStopReason.Force, 0L)]
    public void MapStopReasonToGraceAndForegroundPropagation(ComputeStopReason reason, long expectedGrace)
    {
        var deleteOptions = KubernetesComputeMapping.ToDeleteOptions(reason, options);

        deleteOptions.GracePeriodSeconds.ShouldBe(expectedGrace);
        deleteOptions.PropagationPolicy.ShouldBe("Foreground");
    }

    [Fact]
    public void SkipJobsWithoutParsableWorkerAnnotation()
    {
        KubernetesComputeMapping.ToWorkerInfo(new V1Job()).ShouldBeNull();
        KubernetesComputeMapping.ToWorkerInfo(new V1Job
        {
            Metadata = new V1ObjectMeta { Annotations = new Dictionary<string, string> { ["other"] = "x" } },
        }).ShouldBeNull();
        KubernetesComputeMapping.ToWorkerInfo(new V1Job
        {
            Metadata = new V1ObjectMeta { Annotations = new Dictionary<string, string> { ["comuki.worker_id"] = "nope" } },
        }).ShouldBeNull();
    }

    [Fact]
    public void ConsiderOnlyJobsWithActivePodsRunning()
    {
        KubernetesComputeMapping.IsRunning(new V1Job { Status = new V1JobStatus { Active = 1 } }).ShouldBeTrue();
        KubernetesComputeMapping.IsRunning(new V1Job { Status = new V1JobStatus { Active = 0, Succeeded = 1 } }).ShouldBeFalse();
        KubernetesComputeMapping.IsRunning(new V1Job { Status = null }).ShouldBeFalse();
    }

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
}
