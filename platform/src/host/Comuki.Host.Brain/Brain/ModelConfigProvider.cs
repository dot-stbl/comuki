using Comuki.Host.Brain.Brain.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Default <see cref="IModelConfigProvider"/>. For every field the
/// resolution order is: per-call <c>*Ref</c> on
/// <see cref="BrainOptions"/> (when set) is routed through the
/// shared <see cref="ISecretResolver"/>; otherwise the boot-time
/// <see cref="BrainModelOptions"/> value carries through. A
/// non-empty <c>*Ref</c> with no provider answer surfaces as
/// <see cref="SecretRefUnsetException"/> — the same path every other
/// hot-ref consumer uses (intake sources, OIDC client secrets).
/// Singleton — no mutable state, the resolver is itself a singleton.
/// </summary>
/// <param name="secrets">Shared-kernel resolver — routes by scheme to the matching provider.</param>
/// <param name="options">Bound <c>brain:*</c> configuration (refs + boot-time model fallback).</param>
public sealed class ModelConfigProvider(
    ISecretResolver secrets,
    IOptions<BrainOptions> options) : IModelConfigProvider
{
    /// <inheritdoc />
    public async Task<ModelConfig> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var bound = options.Value;

        var endpoint = await ModelConfigProviderHelpers.ResolveFieldAsync(
            secrets, bound.ModelEndpointRef, bound.Model.Endpoint, cancellationToken);
        var apiKey = await ModelConfigProviderHelpers.ResolveFieldAsync(
            secrets, bound.ModelApiKeyRef, bound.Model.ApiKey, cancellationToken);
        var modelId = await ModelConfigProviderHelpers.ResolveFieldAsync(
            secrets, bound.ModelIdRef, bound.Model.ModelId, cancellationToken);
        // The chat-kind lighter model falls back to the flagship when no
        // ChatModelIdRef is configured; the brain agent picks between
        // ModelId and ChatModelId by request kind (see BrainAgent.RunAsync).
        var chatModelId = await ModelConfigProviderHelpers.ResolveFieldAsync(
            secrets, bound.ChatModelIdRef, modelId, cancellationToken);

        return new ModelConfig(endpoint, apiKey, modelId, chatModelId);
    }
}

/// <summary>
/// Pure helpers for <see cref="ModelConfigProvider"/> — isolated so the
/// production class stays free of private business logic
/// (<c>code-shape.md</c> §1a). Every helper is stateless and reads
/// only through its parameters.
/// </summary>
file static class ModelConfigProviderHelpers
{
    /// <summary>
    /// Resolve a single field: <paramref name="reference"/> set → call
    /// the secret resolver; otherwise return the boot-time fallback.
    /// </summary>
    /// <param name="secrets">Shared-kernel resolver — routes by scheme to the matching provider.</param>
    /// <param name="reference">Per-call <c>*Ref</c> (e.g. <c>vault:models/brain#endpoint</c>) — null/empty keeps the boot-time path.</param>
    /// <param name="fallback">Boot-time value from <see cref="BrainModelOptions"/>; returned as-is when <paramref name="reference"/> is empty.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<string> ResolveFieldAsync(
        ISecretResolver secrets,
        string? reference,
        string? fallback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return fallback ?? throw BrainModelUnconfiguredException();
        }

        var resolved = await secrets.ResolveAsync(reference, cancellationToken);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            // Defensive: the resolver's contract is to throw SecretRefUnsetException
            // on a non-empty reference whose provider returns null. This branch is
            // only reachable if a future provider bypasses that contract.
            throw new SecretRefUnsetException(reference);
        }

        return resolved;
    }

    /// <summary>The setup hint raised when no source has any model field.</summary>
    private static InvalidOperationException BrainModelUnconfiguredException()
    {
        return new InvalidOperationException(
            "brain model is not configured: set brain:model (endpoint/apiKey/modelId) "
            + $"or {BrainOptions.ModelEndpointEnvVariable}/{BrainOptions.ModelApiKeyEnvVariable}/{BrainOptions.ModelIdEnvVariable} env vars, "
            + "or the corresponding ModelEndpointRef / ModelApiKeyRef / ModelIdRef secret refs");
    }
}
