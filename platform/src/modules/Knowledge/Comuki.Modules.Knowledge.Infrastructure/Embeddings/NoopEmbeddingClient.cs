using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Comuki.Modules.Knowledge.Application;

namespace Comuki.Modules.Knowledge.Infrastructure.Embeddings;

/// <summary>
/// Deterministic random-vector embedding — no network, no API key.
/// The vector is derived from a SHA-256 of the input text expanded into
/// the configured dimensionality; same text → same vector, so dev
/// searches and tests are repeatable without hitting OpenAI.
/// </summary>
/// <remarks>
/// Not cryptographic — the SHA-256 is only used as a stable, fast
/// hash to seed the <see cref="Random"/>. Each call rebuilds the seed
/// from the text; the per-component scalar mix gives <c>float[]</c> of
/// the requested length with the same dimensionality as the pgvector
/// column (default 1536).
/// </remarks>
[SuppressMessage(
    "Security",
    "CA5394:Random is not cryptographically secure",
    Justification = "Deterministic non-security jitter; the seed is a SHA-256 of the input text.")]
public sealed class NoopEmbeddingClient : IEmbeddingClient
{

    private readonly int dimensions;

    /// <summary>Constructs a noop embedder at the requested dimensionality.</summary>
    /// <param name="dimensions"></param>
    public NoopEmbeddingClient(int dimensions)
    {
        if (dimensions is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "dimensions must be in [1, 4096]");
        }

        this.dimensions = dimensions;
    }

    /// <inheritdoc />
    public string ProviderName => "noop";

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(VectorFor(text));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var vectors = new float[texts.Count][];
        for (var index = 0; index < texts.Count; index++)
        {
            vectors[index] = VectorFor(texts[index]);
        }

        return Task.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    private float[] VectorFor(string text)
    {
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var buffer = new byte[dimensions * sizeof(float)];
        // Stretch the 32-byte digest over the requested vector by
        // repeating it; this stays deterministic and cheap.
        for (var offset = 0; offset < buffer.Length; offset += seed.Length)
        {
            var chunk = Math.Min(seed.Length, buffer.Length - offset);
            Buffer.BlockCopy(seed, 0, buffer, offset, chunk);
        }

        var random = new Random(BitConverter.ToInt32(seed, 0));
        var vector = new float[dimensions];
        for (var index = 0; index < dimensions; index++)
        {
            // Map the byte to [-1, 1]; the random mix keeps components spread
            // across the unit sphere rather than clustered near zero.
            vector[index] = buffer[index * sizeof(float)] / 255f * 2f - 1f;
        }

        // Tiny jitter driven by the digest so two texts that happen to
        // share the first int still diverge.
        for (var index = 0; index < dimensions; index++)
        {
            vector[index] += (float)((float)(random.NextDouble() - 0.5d) * 0.01d);
        }

        return vector;
    }
}
