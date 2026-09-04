namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// The embedding provider the knowledge layer talks to. Wire key:
/// <see cref="EmbeddingProviderKindKeys"/>. <c>Noop</c> returns a
/// deterministic random vector so dev + test runs never need an API key.
/// </summary>
public enum EmbeddingProviderKind
{
    /// <summary>OpenAI text-embedding-3-small (1536-dim) — production default.</summary>
    OpenAi = 1,

    /// <summary>Voyage AI — same vector-size contract, alt provider.</summary>
    Voyage = 2,

    /// <summary>Deterministic random vector — dev / test only.</summary>
    Noop = 3,
}
