namespace Comuki.Modules.Knowledge.Application;

/// <summary>
/// Embedding provider abstraction — vectorises text into a
/// fixed-length <c>float[]</c>. Implementations include OpenAI (prod)
/// and a deterministic Noop (dev/test). The vector dimensionality is
/// provider-specific but always
/// <see cref="Memory.Infrastructure.Persistence.Stores.MemoryEmbeddingSql.Dimensions"/>
/// (1536) so pgvector cosine-distance stays valid.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>The configured provider kind — for diagnostics + telemetry tagging.</summary>
    public string ProviderName { get; }

    /// <summary>Embed a single text. Throws on transport / auth failure.</summary>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">The provider rejected the request.</exception>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embed many texts in one round-trip where the provider supports it; otherwise per-text loop.</summary>
    /// <param name="texts"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
