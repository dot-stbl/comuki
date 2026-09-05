using System.ComponentModel.DataAnnotations;
using Comuki.Modules.Knowledge.Domain;

namespace Comuki.Modules.Knowledge.Infrastructure.Configuration;

/// <summary>
/// Configuration for the embedding provider behind
/// <see cref="Application.IEmbeddingClient"/>.
/// <c>Provider = noop</c> returns deterministic random vectors and is
/// the dev/test default — no API key, no network.
/// </summary>
public sealed class KnowledgeEmbeddingOptions
{
    /// <summary>Configuration section name (used by the host installer).</summary>
    public const string SectionName = "Knowledge:Embedding";

    /// <summary>Provider kind — openai | voyage | noop. Wire key: <see cref="EmbeddingProviderKindKeys"/>.</summary>
    [Required]
    public string Provider { get; init; } = EmbeddingProviderKindKeys.Noop;

    /// <summary>Name of the env var that carries the API key (never the key itself).</summary>
    public string? ApiKeyEnvRef { get; init; }

    /// <summary>Embedding model name — provider-specific (e.g. <c>text-embedding-3-small</c>).</summary>
    [Required]
    public string Model { get; init; } = "text-embedding-3-small";

    /// <summary>Vector dimensionality — must match the pgvector column dimension.</summary>
    [Range(1, 4096)]
    public int Dimensions { get; init; } = 1536;

    /// <summary>Per-call batch size — limits the number of texts in one provider round-trip.</summary>
    [Range(1, 256)]
    public int BatchSize { get; init; } = 32;

    /// <summary>Reads the API key from <see cref="ApiKeyEnvRef"/>; null when unset / <c>noop</c>.</summary>
    public string? ResolveApiKey()
    {
        return string.IsNullOrWhiteSpace(ApiKeyEnvRef)
            ? null
            : Environment.GetEnvironmentVariable(ApiKeyEnvRef);
    }

    /// <summary>Maps the wire key back to the enum for the registration switch.</summary>
    public EmbeddingProviderKind Kind => EmbeddingProviderKindKeys.ParseRequired(Provider);
}
