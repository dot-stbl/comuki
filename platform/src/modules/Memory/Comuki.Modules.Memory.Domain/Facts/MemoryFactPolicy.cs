using Comuki.Modules.Memory.Domain.Facts.Kinds;

namespace Comuki.Modules.Memory.Domain.Facts;

/// <summary>
/// Domain policy constants for memory facts: the ephemeral TTL, the
/// embedding vector dimension and the visibility rule shared by search,
/// digest and the sweep worker.
/// </summary>
public static class MemoryFactPolicy
{
    /// <summary>
    /// How long an ephemeral fact stays visible before the sweep deletes
    /// it. 14 days per the add-chat-memory contract.
    /// </summary>
    public static readonly TimeSpan EphemeralTtl = TimeSpan.FromDays(14);

    /// <summary>
    /// Embedding vector dimension of the <c>memory_facts.embedding</c>
    /// column. Tied to the embedding provider: swapping to a provider with
    /// a different dimension requires dropping the column, re-embedding
    /// every fact (reindex) and recreating it — embeddings are never
    /// migrated. Search keeps working without embeddings via the
    /// scope+kind+freshest fallback ranking.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    /// <summary>True when an ephemeral fact's TTL has elapsed.</summary>
    /// <param name="fact"></param>
    /// <param name="now"></param>
    public static bool IsExpired(MemoryFact fact, DateTimeOffset now)
    {
        return fact.Kind == MemoryFactKind.Ephemeral
            && now - fact.CreatedAt >= EphemeralTtl;
    }

    /// <summary>
    /// True when a fact participates in search and digest: not superseded
    /// and — for ephemeral — not past its TTL.
    /// </summary>
    /// <param name="fact"></param>
    /// <param name="now"></param>
    public static bool IsVisible(MemoryFact fact, DateTimeOffset now)
    {
        return fact.SupersededAt is null && !IsExpired(fact, now);
    }
}
