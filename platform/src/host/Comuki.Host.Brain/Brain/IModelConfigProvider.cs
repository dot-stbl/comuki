namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Resolves the model connection (endpoint, API key, model id) at call
/// time, optionally through the shared <c>ISecretResolver</c> so an
/// operator can switch models in Vault / Consul without restarting the
/// host (issue #53). The brain agent loop calls
/// <see cref="ResolveAsync"/> on every invocation; the resolver's
/// TTL cache bounds the round-trip to one resolve per TTL (60s
/// default for the Vault / Consul providers) — a model rotation
/// therefore picks up within one TTL without a process restart.
/// </summary>
public interface IModelConfigProvider
{
    /// <summary>
    /// Resolve the effective model configuration for this call. The
    /// returned <see cref="ModelConfig"/> is the union of:
    /// per-call <c>*Ref</c> fields on <c>BrainOptions</c> (resolved
    /// via <c>ISecretResolver</c> when set) and the boot-time
    /// <c>BrainOptions.Model</c> values (the existing fallback path,
    /// preserved for deployments without a secrets backend).
    /// </summary>
    /// <param name="cancellationToken"></param>
    public Task<ModelConfig> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Effective model configuration for a single brain call. The
/// <see cref="Endpoint"/>, <see cref="ApiKey"/> and
/// <see cref="ModelId"/> fields drive the OpenAI-compatible chat
/// client the agent builds per call. <see cref="ChatModelId"/> is the
/// lighter model the chat graph asks for when
/// <c>BrainOptions.ChatModelIdRef</c> is set (the default model
/// otherwise — see <see cref="ModelConfigProvider"/>).
/// </summary>
/// <param name="Endpoint">OpenAI-compatible base endpoint.</param>
/// <param name="ApiKey">API key for the upstream.</param>
/// <param name="ModelId">Flagship model id (plan / brief / repair / default).</param>
/// <param name="ChatModelId">Lighter model id used for chat-kind requests.</param>
public sealed record ModelConfig(string Endpoint, string ApiKey, string ModelId, string ChatModelId);
