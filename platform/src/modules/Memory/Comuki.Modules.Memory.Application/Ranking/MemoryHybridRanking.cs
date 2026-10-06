using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// Reciprocal Rank Fusion (RRF) of the lexical (<c>ts_rank</c>) and
/// vector (1 − cosine distance) rank lists on the same row set. Used by
/// the hybrid retrieval path inside the memory module — the lex rank
/// and the vector rank carry their raw scores on
/// <see cref="MemoryFactView.LexicalRank"/> and
/// <see cref="MemoryFactView.VectorRank"/>; <see cref="Fuse"/> rewrites
/// <see cref="MemoryFactView.FusedScore"/> and reorders the input by it.
/// The Context Fabric delivery is deferred (depends on the Context Pack
/// manifest from the mission-foundation work — issue #96 / #160), so
/// the constants <see cref="K"/> and <see cref="DefaultLexicalWeight"/>
/// / <see cref="DefaultVectorWeight"/> live here as the manifest's
/// interim source of truth; once the manifest lands it MUST point at
/// these values.
/// </summary>
/// <remarks>
/// RRF is the standard "two rankers, one ordering" trick — each list
/// contributes <c>1 / (k + rank)</c>, and the sums form the fused score.
/// Compared with weighted-sum fusion RRF is robust to tail outliers
/// (one ranker scoring 0 vs another scoring 1000 — without k the gap
/// would dominate; with k=60 the relative gap is bounded).
/// </remarks>
public static class MemoryHybridRanking
{
    /// <summary>
    /// Standard RRF smoothing constant: 60 (Cormack et al., 2009). The
    /// Context Fabric manifest MUST record this value when the
    /// manifest lands — keeping it here makes that a one-line
    /// reference, not a magic number to chase.
    /// </summary>
    public const int K = 60;

    /// <summary>Default per-list weight for the lexical list; the Context Fabric manifest records it.</summary>
    public const float DefaultLexicalWeight = 1.0f;

    /// <summary>Default per-list weight for the vector list; the Context Fabric manifest records it.</summary>
    public const float DefaultVectorWeight = 1.0f;

    /// <summary>
    /// Fuses two rank lists into a single ordered sequence. Rows that
    /// appear in only one list get the missing list's contribution as
    /// zero (the row's <see cref="MemoryFactView.LexicalRank"/> or
    /// <see cref="MemoryFactView.VectorRank"/> is zero when the path
    /// that produced the row was the other one).
    /// </summary>
    /// <param name="lexicalRows">
    /// Rows ranked by <c>ts_rank</c> descending; the index in the input
    /// is the list rank. Empty list = the lexical path was skipped
    /// (column missing, query empty, etc.).
    /// </param>
    /// <param name="vectorRows">
    /// Rows ranked by 1 − cosine distance descending; same contract.
    /// </param>
    /// <param name="lexicalWeight">Per-list weight on the lexical side; defaults to <see cref="DefaultLexicalWeight"/>.</param>
    /// <param name="vectorWeight">Per-list weight on the vector side; defaults to <see cref="DefaultVectorWeight"/>.</param>
    /// <param name="limit">Maximum rows returned.</param>
    public static IReadOnlyList<MemoryFactView> Fuse(
        IReadOnlyList<MemoryFactView> lexicalRows,
        IReadOnlyList<MemoryFactView> vectorRows,
        float lexicalWeight = DefaultLexicalWeight,
        float vectorWeight = DefaultVectorWeight,
        int limit = int.MaxValue)
    {
        if (limit <= 0)
        {
            return [];
        }

        var fused = new Dictionary<MemoryFactId, FusedEntry>();

        // Lexical list: rank = position in the input (0 = top).
        for (var rank = 0; rank < lexicalRows.Count; rank++)
        {
            var row = lexicalRows[rank];
            if (row is null)
            {
                continue;
            }

            var contribution = lexicalWeight / (K + rank + 1);
            fused[row.Id] = new FusedEntry(row, contribution);
        }

        // Vector list: accumulate on top of whatever the lexical side
        // produced for the same row id (the same fact can rank in both
        // lists). Rows new to the vector side get the lexical contribution
        // as zero by default.
        for (var rank = 0; rank < vectorRows.Count; rank++)
        {
            var row = vectorRows[rank];
            if (row is null)
            {
                continue;
            }

            var contribution = vectorWeight / (K + rank + 1);
            fused[row.Id] = fused.TryGetValue(row.Id, out var existing)
                ? new FusedEntry(existing.View, existing.Score + contribution)
                : new FusedEntry(row, contribution);
        }

        // Rewrite FusedScore on each row and order by it desc, then by
        // CreatedAt desc as a stable tie-breaker.
        return [.. fused.Values
            .Select(static entry => entry.View with { FusedScore = entry.Score })
            .OrderByDescending(static view => view.FusedScore)
            .ThenByDescending(static view => view.CreatedAt)
            .Take(limit)];
    }
}
