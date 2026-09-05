namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// Stable wire keys for <see cref="EmbeddingProviderKind"/> — the
/// <c>Knowledge:Embedding:Provider</c> config value and the MCP
/// <c>knowledge.ingest</c> tool surface use these strings.
/// </summary>
public static class EmbeddingProviderKindKeys
{
    /// <summary>Key of <see cref="EmbeddingProviderKind.OpenAi"/>.</summary>
    public const string OpenAi = "openai";

    /// <summary>Key of <see cref="EmbeddingProviderKind.Voyage"/>.</summary>
    public const string Voyage = "voyage";

    /// <summary>Key of <see cref="EmbeddingProviderKind.Noop"/>.</summary>
    public const string Noop = "noop";

    /// <summary>Maps a kind to its wire key.</summary>
    /// <param name="kind"></param>
    public static string Key(EmbeddingProviderKind kind)
    {
        return kind switch
        {
            EmbeddingProviderKind.OpenAi => OpenAi,
            EmbeddingProviderKind.Voyage => Voyage,
            EmbeddingProviderKind.Noop => Noop,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static EmbeddingProviderKind? Parse(string key)
    {
        return key switch
        {
            OpenAi => EmbeddingProviderKind.OpenAi,
            Voyage => EmbeddingProviderKind.Voyage,
            Noop => EmbeddingProviderKind.Noop,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static EmbeddingProviderKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown embedding provider key '{key}'");
    }
}
