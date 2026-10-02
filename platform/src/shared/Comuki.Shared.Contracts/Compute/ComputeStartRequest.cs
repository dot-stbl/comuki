using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Everything a worker container needs at start. Mirrors the env contract:
/// COMUKI_WORKER_TOKEN, COMUKI_PROJECT_ID, COMUKI_PROFILE_KEY,
/// COMUKI_PROFILES_REF, COMUKI_ENV_CLASS, COMUKI_WORKER_IMAGE,
/// COMUKI_ORCH_GRPC.
/// </summary>
public sealed record ComputeStartRequest()
{
    public required ProjectId ProjectId { get; init; }

    /// <summary>
    /// WorkerId the caller already bound a token to. The provider must reuse
    /// it instead of minting its own, so the token identity and the container
    /// identity agree. Null lets the provider mint a fresh id.
    /// </summary>
    public WorkerId? PreIssuedWorkerId { get; init; }

    /// <summary>Profile key the scale decision was made for (e.g. <c>implement</c>).</summary>
    public required string ProfileKey { get; init; }

    /// <summary>Pinned git ref of the profiles repo (client overlay or Comuki defaults).</summary>
    public required string ProfilesGitRef { get; init; }

    /// <summary>
    /// Environment-class id the project binds to (e.g. <c>net10-sdk-bun</c>);
    /// the provider resolves the digest through <c>IEnvironmentCatalog</c>
    /// and stamps it as <c>COMUKI_WORKER_IMAGE</c> + the
    /// <c>comuki.env_class</c> claim-matching label.
    /// </summary>
    public required string EnvClass { get; init; }

    /// <summary>Worker image with digest — labels carry it for claim matching.</summary>
    public required string Image { get; init; }

    /// <summary>
    /// SlotAdmission id that cleared <c>ISlotAdmissionEvaluator</c> for
    /// this slot (add-worker-admission task 1.3, spec
    /// §"SlotAdmission is the only start input"). Nullable on this slice —
    /// the enforcement tightens when a worker-pools slice wraps the 1:1
    /// container, but until then an absent id means "admission gate lives
    /// on the Translator path" and the compute provider must tolerate it
    /// so the existing Docker / k8s flows keep working.
    /// </summary>
    public Guid? AdmissionId { get; init; }

    /// <summary>Short-lived opaque token; validated by the Host gRPC endpoint.</summary>
    public required string WorkerToken { get; init; }

    public required Uri OrchestratorGrpcUrl { get; init; }

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
