using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;

namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// The embedding-free fact ranking used by search, digest and the /memory
/// list surface: standing facts before ephemeral ones, then freshest
/// first. Memory MUST answer meaningfully without pgvector/embeddings per
/// the add-chat-memory contract — this ordering is that guarantee.
/// </summary>
public static class MemoryFallbackRanking
{
    /// <summary>Ranks visible facts: standing first, then freshest.</summary>
    /// <param name="facts">Visible facts of one query scope.</param>
    /// <param name="limit">Maximum entries returned.</param>
    public static IReadOnlyList<MemoryFactView> Rank(IEnumerable<MemoryFactView> facts, int limit)
    {
        return [.. facts
            .OrderByDescending(static fact => fact.Kind == MemoryFactKind.Standing)
            .ThenByDescending(static fact => fact.CreatedAt)
            .Take(limit)];
    }
}
