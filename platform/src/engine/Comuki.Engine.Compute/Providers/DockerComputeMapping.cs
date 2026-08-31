using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet.Models;

namespace Comuki.Engine.Compute.Providers;

/// <summary>
/// Pure mapping between the compute port types and the Docker.DotNet
/// parameter/response models. No I/O — the provider does the calls, this
/// class shapes them (testable in isolation).
/// </summary>
internal static class DockerComputeMapping
{
    /// <summary>Builds the create-parameters for one worker container.</summary>
    /// <param name="request"></param>
    /// <param name="workerId"></param>
    /// <param name="options"></param>
    public static CreateContainerParameters ToCreateParameters(
        ComputeStartRequest request,
        WorkerId workerId,
        Options.DockerComputeOptions options)
    {
        return new CreateContainerParameters
        {
            Image = request.Image,
            Name = ToContainerName(request.ProjectId, workerId),
            Env = BuildEnvironment(request),
            Labels = BuildLabels(request, workerId),
            HostConfig = new HostConfig { NetworkMode = options.NetworkMode },
        };
    }

    /// <summary>Container name: comuki-{projectId}-{short worker suffix}, unique per start.</summary>
    /// <param name="projectId"></param>
    /// <param name="workerId"></param>
    public static string ToContainerName(ProjectId projectId, WorkerId workerId)
    {
        return $"comuki-{projectId.Value:N}-{workerId.Value.ToString("N")[..12]}";
    }

    /// <summary>List parameters selecting containers of one project (running only).</summary>
    /// <param name="projectId"></param>
    public static ContainersListParameters ToProjectListParameters(ProjectId projectId)
    {
        return ToLabelListParameters($"{ComputeLabels.Project}={projectId.Value}", all: false);
    }

    /// <summary>List parameters selecting containers of one worker (any state, for stop/cleanup).</summary>
    /// <param name="workerId"></param>
    public static ContainersListParameters ToWorkerListParameters(WorkerId workerId)
    {
        return ToLabelListParameters($"{DockerComputeProvider.WorkerIdLabel}={workerId.Value}", all: true);
    }

    /// <summary>List parameters selecting every comuki worker (label presence, running only).</summary>
    public static ContainersListParameters ToWorkerListParameters()
    {
        return ToLabelListParameters(ComputeLabels.Project, all: false);
    }

    /// <summary>Maps a listed container to a <see cref="WorkerInfo"/>; null when labels are missing.</summary>
    /// <param name="container"></param>
    public static WorkerInfo? ToWorkerInfo(ContainerListResponse container)
    {
        if (container.Labels is null
            || !container.Labels.TryGetValue(DockerComputeProvider.WorkerIdLabel, out var workerIdValue)
            || !Guid.TryParse(workerIdValue, out var workerId))
        {
            return null;
        }

        // ProfilesGitRef carries the sanitized label value (slashes replaced) — claim
        // matching compares sanitized values on both sides, see ComputeLabels.Sanitize.
        return new WorkerInfo(
            new WorkerId(workerId),
            container.ID,
            LabelOrDefault(container.Labels, ComputeLabels.Profile),
            LabelOrDefault(container.Labels, ComputeLabels.Image),
            LabelOrDefault(container.Labels, ComputeLabels.ProfilesRef));
    }

    /// <summary>Label value or empty string when the label is absent.</summary>
    /// <param name="labels"></param>
    /// <param name="key"></param>
    public static string LabelOrDefault(IDictionary<string, string> labels, string key)
    {
        return labels.TryGetValue(key, out var value) ? value : string.Empty;
    }

    /// <summary>List parameters selecting containers by a docker label filter expression.</summary>
    /// <param name="labelFilter"></param>
    /// <param name="all"></param>
    public static ContainersListParameters ToLabelListParameters(string labelFilter, bool all)
    {
        return new ContainersListParameters
        {
            All = all,
            Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
            {
                ["label"] = new Dictionary<string, bool>(StringComparer.Ordinal) { [labelFilter] = true },
            },
        };
    }

    /// <summary>Env of the container: the COMUKI_* contract first, then the caller-supplied extras.</summary>
    /// <param name="request"></param>
    public static List<string> BuildEnvironment(ComputeStartRequest request)
    {
        var environment = new List<string>
        {
            $"COMUKI_WORKER_TOKEN={request.WorkerToken}",
            $"COMUKI_PROJECT_ID={request.ProjectId.Value}",
            $"COMUKI_PROFILE_KEY={request.ProfileKey}",
            $"COMUKI_PROFILES_REF={request.ProfilesGitRef}",
            $"COMUKI_ORCH_GRPC={request.OrchestratorGrpcUrl}",
        };
        environment.AddRange(request.Env.Select(static pair => $"{pair.Key}={pair.Value}"));
        return environment;
    }

    /// <summary>Match labels stamped on the container (claim matching reads them back via List).</summary>
    /// <param name="request"></param>
    /// <param name="workerId"></param>
    public static Dictionary<string, string> BuildLabels(ComputeStartRequest request, WorkerId workerId)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ComputeLabels.Project] = request.ProjectId.Value.ToString(),
            [ComputeLabels.Profile] = ComputeLabels.Sanitize(request.ProfileKey),
            [ComputeLabels.Image] = ComputeLabels.Sanitize(request.Image),
            [ComputeLabels.ProfilesRef] = ComputeLabels.Sanitize(request.ProfilesGitRef),
            [DockerComputeProvider.WorkerIdLabel] = workerId.Value.ToString(),
        };
    }
}

