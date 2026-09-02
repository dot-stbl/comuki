using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s.Models;

namespace Comuki.Engine.Compute.Providers.Kubernetes;

/// <summary>
/// Pure mapping between the compute port types and the batch/v1 Job model.
/// No I/O — the provider does the calls, this class shapes them (testable
/// in isolation), mirroring <see cref="DockerComputeMapping"/> on the k8s side.
/// </summary>
internal static class KubernetesComputeMapping
{
    /// <summary>Worker Job name: comuki-w-{12-char worker-id suffix}, derivable from the id alone.</summary>
    /// <param name="workerId"></param>
    public static string ToJobName(WorkerId workerId)
    {
        return $"comuki-w-{workerId.Value.ToString("N")[..12]}";
    }

    /// <summary>Label-selector string selecting the worker Jobs of one project.</summary>
    /// <param name="projectId"></param>
    public static string ToProjectLabelSelector(ProjectId projectId)
    {
        return $"{ComputeLabels.Project}={projectId.Value}";
    }

    /// <summary>Builds the batch/v1 Job of one worker: backoffLimit 0, TTL cleanup, sanitized labels, env contract.</summary>
    /// <param name="request"></param>
    /// <param name="workerId"></param>
    /// <param name="options"></param>
    public static V1Job ToJob(ComputeStartRequest request, WorkerId workerId, KubernetesComputeOptions options)
    {
        var labels = BuildLabels(request);

        return new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = ToJobName(workerId),
                Labels = labels,
                Annotations = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesComputeProvider.WorkerIdAnnotation] = workerId.Value.ToString(),
                },
            },
            Spec = new V1JobSpec
            {
                // A failed worker is not retried by the platform — the work
                // item lease expires and another worker re-claims it.
                BackoffLimit = 0,
                TtlSecondsAfterFinished = options.TtlSecondsAfterFinished,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = new V1PodSpec
                    {
                        ServiceAccountName = options.ServiceAccount,
                        NodeSelector = options.NodeSelector.Count > 0
                            ? new Dictionary<string, string>(options.NodeSelector, StringComparer.Ordinal)
                            : null,
                        RestartPolicy = "Never",
                        Containers =
                        [
                            new V1Container
                            {
                                Name = "worker",
                                Image = request.Image,
                                Env = BuildEnvironment(request),
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                                    {
                                        ["cpu"] = new($"{options.CpuRequestMillis}m"),
                                        ["memory"] = new($"{options.MemoryRequestMiB}Mi"),
                                    },
                                },
                            },
                        ],
                    },
                },
            },
        };
    }

    /// <summary>Delete options mapped from the stop reason: soft reasons get the configured grace, Force gets 0; always Foreground propagation so the pod dies with the Job.</summary>
    /// <param name="reason"></param>
    /// <param name="options"></param>
    public static V1DeleteOptions ToDeleteOptions(ComputeStopReason reason, KubernetesComputeOptions options)
    {
        return new V1DeleteOptions
        {
            GracePeriodSeconds = ToGraceSeconds(reason, options),
            PropagationPolicy = "Foreground",
        };
    }

    /// <summary>Grace seconds for the stop reason: Force hard-kills (0), everything else uses <see cref="KubernetesComputeOptions.TerminationGraceSeconds"/>.</summary>
    /// <param name="reason"></param>
    /// <param name="options"></param>
    public static long ToGraceSeconds(ComputeStopReason reason, KubernetesComputeOptions options)
    {
        return reason == ComputeStopReason.Force ? 0 : options.TerminationGraceSeconds;
    }

    /// <summary>Whether the Job still has an active pod — finished-but-not-yet-collected Jobs are not running workers.</summary>
    /// <param name="job"></param>
    public static bool IsRunning(V1Job job)
    {
        return job.Status?.Active is > 0;
    }

    /// <summary>Maps a listed Job to a <see cref="WorkerInfo"/>; null when the worker-id annotation is missing or unparsable.</summary>
    /// <param name="job"></param>
    public static WorkerInfo? ToWorkerInfo(V1Job job)
    {
        if (job.Metadata?.Annotations is null
            || !job.Metadata.Annotations.TryGetValue(KubernetesComputeProvider.WorkerIdAnnotation, out var workerIdValue)
            || !Guid.TryParse(workerIdValue, out var workerId))
        {
            return null;
        }

        var labels = job.Metadata.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new WorkerInfo(
            new WorkerId(workerId),
            job.Metadata.Name ?? string.Empty,
            LabelOrDefault(labels, ComputeLabels.Profile),
            LabelOrDefault(labels, ComputeLabels.Image),
            LabelOrDefault(labels, ComputeLabels.ProfilesRef));
    }

    /// <summary>Label value or empty string when the label is absent.</summary>
    /// <param name="labels"></param>
    /// <param name="key"></param>
    public static string LabelOrDefault(IDictionary<string, string> labels, string key)
    {
        return labels.TryGetValue(key, out var value) ? value : string.Empty;
    }

    /// <summary>Claim-matching labels shared by the Job metadata and the pod template (sanitized — k8s label values cannot contain slashes).</summary>
    /// <param name="request"></param>
    public static Dictionary<string, string> BuildLabels(ComputeStartRequest request)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ComputeLabels.Project] = request.ProjectId.Value.ToString(),
            [ComputeLabels.Profile] = ComputeLabels.Sanitize(request.ProfileKey),
            [ComputeLabels.Image] = ComputeLabels.Sanitize(request.Image),
            [ComputeLabels.ProfilesRef] = ComputeLabels.Sanitize(request.ProfilesGitRef),
        };
    }

    /// <summary>Env of the worker container: the COMUKI_* contract first, then the caller-supplied extras.</summary>
    /// <param name="request"></param>
    public static List<V1EnvVar> BuildEnvironment(ComputeStartRequest request)
    {
        var environment = new List<V1EnvVar>
        {
            new("COMUKI_WORKER_TOKEN", request.WorkerToken),
            new("COMUKI_PROJECT_ID", request.ProjectId.Value.ToString()),
            new("COMUKI_PROFILE_KEY", request.ProfileKey),
            new("COMUKI_PROFILES_REF", request.ProfilesGitRef),
            new("COMUKI_WORKER_IMAGE", request.Image),
            new("COMUKI_ORCH_GRPC", request.OrchestratorGrpcUrl.ToString()),
        };
        environment.AddRange(request.Env.Select(static pair => new V1EnvVar(pair.Key, pair.Value)));
        return environment;
    }
}
