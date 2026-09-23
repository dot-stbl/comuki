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
    /// <summary>PolicyTypes value of the worker fence: egress-only default-deny.</summary>
    public const string EgressPolicyType = "Egress";

    /// <summary>Capabilities drop value clearing the worker container's Linux capability set.</summary>
    internal const string DropAllCapabilities = "ALL";

    /// <summary>Seccomp profile type of the worker container — the runtime default.</summary>
    internal const string SeccompRuntimeDefault = "RuntimeDefault";

    /// <summary>Well-known label every namespace carries (Kubernetes API convention) — selects kube-system for the DNS allow.</summary>
    internal const string NamespaceNameLabel = "kubernetes.io/metadata.name";

    /// <summary>Pod label of the cluster DNS deployment (kube-dns / CoreDNS carry the same value).</summary>
    internal const string KubeDnsPodLabel = "k8s-app";

    /// <summary>Value of <see cref="KubeDnsPodLabel"/> on the cluster DNS pods.</summary>
    internal const string KubeDnsPodLabelValue = "kube-dns";

    /// <summary>Name of the namespace the DNS allow targets.</summary>
    internal const string KubeSystemNamespace = "kube-system";

    /// <summary>DNS port opened by the fence, tcp and udp.</summary>
    internal const int DnsPort = 53;

    /// <summary>Wire protocol of the DNS allow, udp variant.</summary>
    internal const string UdpProtocol = "UDP";

    /// <summary>Wire protocol of the DNS allow, tcp variant.</summary>
    internal const string TcpProtocol = "TCP";

    /// <summary>Pod restart policy — a failed worker is never restarted in place (the work-item lease moves on).</summary>
    internal const string RestartPolicyNever = "Never";

    /// <summary>Container name inside the worker pod.</summary>
    internal const string WorkerContainerName = "worker";

    /// <summary>Resource dictionary key of the cpu request/limit quantities.</summary>
    internal const string CpuResourceKey = "cpu";

    /// <summary>Resource dictionary key of the memory request/limit quantities.</summary>
    internal const string MemoryResourceKey = "memory";

    /// <summary>Propagation policy of Job deletion — foreground, so the pod dies with the Job.</summary>
    internal const string ForegroundPropagation = "Foreground";

    /// <summary>Worker Job name: comuki-w-{12-char worker-id suffix}, derivable from the id alone.</summary>
    public static string ToJobName(WorkerId workerId)
    {
        // UUID7 packs the timestamp into the first 12 hex chars of the "N"
        // form — two ids minted in the same millisecond collide on a prefix
        // slice. Take the trailing 12 (random bits) so job names stay unique.
        return $"comuki-w-{workerId.Value.ToString("N")[^12..]}";
    }

    /// <summary>Per-worker egress fence name: comuki-w-egress-{12-char worker-id suffix}, derived from the same slice as the Job name.</summary>
    public static string ToNetworkPolicyName(WorkerId workerId)
    {
        return $"comuki-w-egress-{workerId.Value.ToString("N")[^12..]}";
    }

    /// <summary>Label-selector string selecting the worker Jobs of one project.</summary>
    public static string ToProjectLabelSelector(ProjectId projectId)
    {
        return $"{ComputeLabels.Project}={projectId.Value}";
    }

    /// <summary>
    /// Builds the per-worker egress fence: a networking.k8s.io/v1
    /// NetworkPolicy that default-denies egress from the worker pods
    /// (podSelector on the same comuki.* labels the Job stamps) and allows
    /// only cluster DNS plus the operator-supplied CIDRs
    /// (<see cref="KubernetesComputeOptions.Egress"/>).
    /// </summary>
    public static V1NetworkPolicy ToNetworkPolicy(ComputeStartRequest request, WorkerId workerId, KubernetesComputeOptions options)
    {
        var labels = BuildLabels(request);

        return new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = ToNetworkPolicyName(workerId),
                Labels = labels,
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = labels },
                PolicyTypes = [EgressPolicyType],
                Egress = BuildEgressRules(options),
            },
        };
    }

    /// <summary>Egress allows of the fence: cluster DNS (53 tcp+udp) first, then one rule per operator-supplied CIDR; everything else is denied.</summary>
    internal static List<V1NetworkPolicyEgressRule> BuildEgressRules(KubernetesComputeOptions options)
    {
        var rules = new List<V1NetworkPolicyEgressRule>
        {
            new()
            {
                To =
                [
                    new V1NetworkPolicyPeer
                    {
                        NamespaceSelector = new V1LabelSelector
                        {
                            MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [NamespaceNameLabel] = KubeSystemNamespace,
                            },
                        },
                        PodSelector = new V1LabelSelector
                        {
                            MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [KubeDnsPodLabel] = KubeDnsPodLabelValue,
                            },
                        },
                    },
                ],
                Ports =
                [
                    new V1NetworkPolicyPort { Protocol = UdpProtocol, Port = DnsPort },
                    new V1NetworkPolicyPort { Protocol = TcpProtocol, Port = DnsPort },
                ],
            },
        };

        rules.AddRange(options.Egress.AllowedCidrs.Select(static cidr => new V1NetworkPolicyEgressRule
        {
            To = [new V1NetworkPolicyPeer { IpBlock = new V1IPBlock(cidr) }],
        }));

        return rules;
    }

    /// <summary>
    /// Builds the batch/v1 Job of one worker: backoffLimit 0, TTL cleanup, sanitized
    /// labels, env contract, cpu/memory resources and the sandboxed security context.
    /// The Job name and the <see cref="KubernetesComputeProvider.WorkerIdAnnotation"/>
    /// (which list/stop read back to the orchestrator's worker) derive from the worker id.
    /// </summary>
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
                        RestartPolicy = RestartPolicyNever,
                        // Non-root, no privilege escalation, no capabilities,
                        // runtime-default seccomp (worker-sandbox hardening).
                        SecurityContext = new V1PodSecurityContext { RunAsNonRoot = true },
                        Containers =
                        [
                            new V1Container
                            {
                                Name = WorkerContainerName,
                                Image = request.Image,
                                Env = BuildEnvironment(request),
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                                    {
                                        [CpuResourceKey] = new($"{options.CpuRequestMillis}m"),
                                        [MemoryResourceKey] = new($"{options.MemoryRequestMiB}Mi"),
                                    },
                                    Limits = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                                    {
                                        [CpuResourceKey] = new($"{options.CpuLimitMillis}m"),
                                        [MemoryResourceKey] = new($"{options.MemoryLimitMiB}Mi"),
                                    },
                                },
                                SecurityContext = new V1SecurityContext
                                {
                                    AllowPrivilegeEscalation = false,
                                    Capabilities = new V1Capabilities(drop: [DropAllCapabilities]),
                                    SeccompProfile = new V1SeccompProfile(SeccompRuntimeDefault),
                                },
                            },
                        ],
                    },
                },
            },
        };
    }

    /// <summary>Delete options mapped from the stop reason: soft reasons get the configured grace, Force gets 0; always Foreground propagation so the pod dies with the Job.</summary>
    public static V1DeleteOptions ToDeleteOptions(ComputeStopReason reason, KubernetesComputeOptions options)
    {
        return new V1DeleteOptions
        {
            GracePeriodSeconds = ToGraceSeconds(reason, options),
            PropagationPolicy = ForegroundPropagation,
        };
    }

    /// <summary>Grace seconds for the stop reason: Force hard-kills (0), everything else uses <see cref="KubernetesComputeOptions.TerminationGraceSeconds"/>.</summary>
    public static long ToGraceSeconds(ComputeStopReason reason, KubernetesComputeOptions options)
    {
        return reason == ComputeStopReason.Force ? 0 : options.TerminationGraceSeconds;
    }

    /// <summary>Whether the Job still has an active pod — finished-but-not-yet-collected Jobs are not running workers.</summary>
    public static bool IsRunning(V1Job job)
    {
        return job.Status?.Active is > 0;
    }

    /// <summary>Maps a listed Job to a <see cref="WorkerInfo"/>; null when the worker-id annotation is missing or unparsable.</summary>
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
    public static string LabelOrDefault(IDictionary<string, string> labels, string key)
    {
        return labels.TryGetValue(key, out var value) ? value : string.Empty;
    }

    /// <summary>Claim-matching labels shared by the Job metadata and the pod template (sanitized — k8s label values cannot contain slashes).</summary>
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
