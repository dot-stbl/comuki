using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Brain.Brain.Options;

/// <summary>
/// Brain host configuration. Config section wins; the
/// <c>COMUKI_BRAIN_MODEL_*</c> env vars are the deployment-facing
/// fallback for the model credentials (never committed). The gRPC port is
/// part of the Comuki port pool (ports.md — 17004).
/// </summary>
public sealed class BrainOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "brain";

    /// <summary>Env var holding the OpenAI-compatible model endpoint.</summary>
    public const string ModelEndpointEnvVariable = "COMUKI_BRAIN_MODEL_ENDPOINT";

    /// <summary>Env var holding the model API key.</summary>
    public const string ModelApiKeyEnvVariable = "COMUKI_BRAIN_MODEL_API_KEY";

    /// <summary>Env var holding the model id.</summary>
    public const string ModelIdEnvVariable = "COMUKI_BRAIN_MODEL_ID";

    /// <summary>Port pool default for the gRPC listener (ports.md).</summary>
    public const int DefaultGrpcPort = 17004;

    /// <summary>Upper bound on model round-trips per brain call — the runaway-loop guard.</summary>
    public const int DefaultMaxToolIterations = 8;

    /// <summary>gRPC listen port (port pool 17000–17200, 17004 is the brain's row).</summary>
    [Range(17000, 17200)]
    public int GrpcPort { get; init; } = DefaultGrpcPort;

    /// <summary>Model round-trips per call cap.</summary>
    [Range(1, 32)]
    public int MaxToolIterations { get; init; } = DefaultMaxToolIterations;

    /// <summary>Control-plane profiles folder the catalog reads (repo-relative in dev).</summary>
    public string ControlPlaneProfilesPath { get; init; } = "control-plane/profiles";

    /// <summary>The leading model (OpenAI-compatible endpoint + credentials).</summary>
    public BrainModelOptions Model { get; init; } = new();

    /// <summary>
    /// Per-call secret reference for the model endpoint (issue #53). When
    /// set, <see cref="IModelConfigProvider"/> resolves the live value
    /// via <c>ISecretResolver</c> on every invocation — Vault / Consul
    /// rotation therefore lands within the resolver's TTL (60s default)
    /// without a host restart. Empty / null keeps the boot-time
    /// <see cref="BrainModelOptions.Endpoint"/> path; existing deployments
    /// (<c>brain:model:*</c> or <c>COMUKI_BRAIN_MODEL_*</c>) keep
    /// working unchanged.
    /// </summary>
    public string? ModelEndpointRef { get; init; }

    /// <summary>
    /// Per-call secret reference for the model API key. See
    /// <see cref="ModelEndpointRef"/> for the resolution order.
    /// </summary>
    public string? ModelApiKeyRef { get; init; }

    /// <summary>
    /// Per-call secret reference for the flagship model id
    /// (plan / brief / repair / default). See
    /// <see cref="ModelEndpointRef"/> for the resolution order.
    /// </summary>
    public string? ModelIdRef { get; init; }

    /// <summary>
    /// Per-call secret reference for the lighter chat-kind model id.
    /// The chat graph asks for the <c>answer</c> kind; this ref lets an
    /// operator route chat through a cheaper model without touching
    /// plan / brief / repair. Falls back to the flagship
    /// (<see cref="ModelIdRef"/> or boot-time <see cref="BrainModelOptions.ModelId"/>)
    /// when unset.
    /// </summary>
    public string? ChatModelIdRef { get; init; }

    /// <summary>
    /// Resolves the effective options: config values first, env vars
    /// filling the model gaps. An empty model section keeps the host
    /// bootable (sweep + catalog run); brain calls fail with a setup hint
    /// until the model is configured. <c>*Ref</c> fields pass through
    /// unchanged — the per-call resolution is
    /// <see cref="IModelConfigProvider"/>'s job, not this one's.
    /// </summary>
    public static BrainOptions Resolve(IConfiguration configuration)
    {
        var bound = configuration.GetSection(SectionName).Get<BrainOptions>() ?? new BrainOptions();

        var endpoint = bound.Model.Endpoint
            ?? Environment.GetEnvironmentVariable(ModelEndpointEnvVariable);
        var apiKey = bound.Model.ApiKey
            ?? Environment.GetEnvironmentVariable(ModelApiKeyEnvVariable);
        var modelId = bound.Model.ModelId
            ?? Environment.GetEnvironmentVariable(ModelIdEnvVariable);

        return new BrainOptions
        {
            GrpcPort = bound.GrpcPort,
            MaxToolIterations = bound.MaxToolIterations,
            ControlPlaneProfilesPath = bound.ControlPlaneProfilesPath,
            Model = new BrainModelOptions
            {
                Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
            },
            ModelEndpointRef = BrainOptionsHelpers.NormalizeRef(bound.ModelEndpointRef),
            ModelApiKeyRef = BrainOptionsHelpers.NormalizeRef(bound.ModelApiKeyRef),
            ModelIdRef = BrainOptionsHelpers.NormalizeRef(bound.ModelIdRef),
            ChatModelIdRef = BrainOptionsHelpers.NormalizeRef(bound.ChatModelIdRef),
        };
    }
}

/// <summary>
/// Pure helpers for <see cref="BrainOptions"/> — isolated so the
/// production class stays free of private business logic
/// (<c>code-shape.md</c> §1a). Every helper is stateless and reads
/// only through its parameters.
/// </summary>
file static class BrainOptionsHelpers
{
    /// <summary>Trim and convert empty / whitespace refs to <c>null</c> so the resolver skip-path is a single null check.</summary>
    /// <param name="reference">Raw <c>*Ref</c> value from config or env; whitespace and null both collapse to null.</param>
    public static string? NormalizeRef(string? reference)
    {
        return string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }
}
