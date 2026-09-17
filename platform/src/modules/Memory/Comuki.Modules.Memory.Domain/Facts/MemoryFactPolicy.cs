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
    /// How much visible life a decayed standing fact keeps: the
    /// consolidation worker demotes a long-unread standing fact to
    /// ephemeral with <see cref="DecayCreatedAt"/> backdating its
    /// <c>created_at</c>, so one day of grace remains before the regular
    /// sweep reaps it — a chance to be read (and re-promoted) before
    /// the fact is gone for good.
    /// </summary>
    public static readonly TimeSpan DecayRemainingTtl = TimeSpan.FromDays(1);

    /// <summary>
    /// Embedding vector dimension of the <c>memory_facts.embedding</c>
    /// column. Pinned to the SAME provider the knowledge schema uses —
    /// one embedding model (<c>text-embedding-3-small</c>, 1536) serves
    /// both stores, so their columns must match. Tied to the embedding
    /// provider: swapping to a provider with a different dimension
    /// requires dropping the column, re-embedding every fact (reindex)
    /// and recreating it — embeddings are never migrated. Search keeps
    /// working without embeddings via the scope+kind+freshest fallback
    /// ranking.
    /// </summary>
    public const int EmbeddingDimensions = 1536;

    /// <summary>True when an ephemeral fact's TTL has elapsed.</summary>
    /// <param name="fact"></param>
    /// <param name="now"></param>
    public static bool IsExpired(MemoryFact fact, DateTimeOffset now)
    {
        return fact.Kind == MemoryFactKind.Ephemeral
            && now - fact.CreatedAt >= EphemeralTtl;
    }

    /// <summary>
    /// The creation instant for an ephemeral fact with a custom TTL. The
    /// sweep deletes on a FIXED <see cref="EphemeralTtl"/> horizon, so a
    /// shorter lifetime is expressed by backdating <c>created_at</c> by the
    /// remaining difference — the visibility checks (search, digest) share
    /// the same horizon and stop returning the fact at the same moment the
    /// sweep reaps it. TTLs at or above <see cref="EphemeralTtl"/> need no
    /// backdating (they simply live the full default horizon).
    /// </summary>
    /// <param name="now">The write instant (the store clock).</param>
    /// <param name="ttl">The requested lifetime; must be positive.</param>
    public static DateTimeOffset EphemeralCreatedAt(DateTimeOffset now, TimeSpan ttl)
    {
        // canon judgement #8: a non-positive ttl is a caller programming error, not an expected miss — ArgumentOutOfRangeException (no stable Code) is the correct type.
        return ttl <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "ephemeral ttl must be positive")
            : ttl >= EphemeralTtl ? now : now - (EphemeralTtl - ttl);
    }

    /// <summary>
    /// The creation instant stamped on a standing fact the consolidation
    /// worker decays to ephemeral: backdated so exactly
    /// <see cref="DecayRemainingTtl"/> of visible life remains under the
    /// fixed <see cref="EphemeralTtl"/> sweep horizon (same backdating
    /// trick <see cref="EphemeralCreatedAt"/> uses for custom TTLs).
    /// </summary>
    /// <param name="now">The decay instant (the worker clock).</param>
    public static DateTimeOffset DecayCreatedAt(DateTimeOffset now)
    {
        return now - (EphemeralTtl - DecayRemainingTtl);
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
