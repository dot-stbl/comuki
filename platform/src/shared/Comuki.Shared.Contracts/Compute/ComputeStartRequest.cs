using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Everything a worker container needs at start. Mirrors the env contract:
/// COMUKI_WORKER_TOKEN, COMUKI_PROJECT_ID, COMUKI_PROFILE_KEY,
/// COMUKI_PROFILES_REF, COMUKI_ORCH_GRPC.
/// </summary>
public sealed class ComputeStartRequest()
{
    public required ProjectId ProjectId { get; init; }

    /// <summary>Profile key the scale decision was made for (e.g. <c>implement</c>).</summary>
    public required string ProfileKey { get; init; }

    /// <summary>Pinned git ref of the profiles repo (client overlay or Comuki defaults).</summary>
    public required string ProfilesGitRef { get; init; }

    /// <summary>Worker image with digest — labels carry it for claim matching.</summary>
    public required string Image { get; init; }

    /// <summary>Short-lived opaque token; validated by the Host gRPC endpoint.</summary>
    public required string WorkerToken { get; init; }

    public required Uri OrchestratorGrpcUrl { get; init; }

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
