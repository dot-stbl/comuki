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
    public Task<ModelConfig> ResolveAsync(CancellationToken cancellationToken = default);
}

