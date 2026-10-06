using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;

namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// The embedding-free fact ranking used by search, digest and the /memory
/// list surface: standing facts before ephemeral ones, then freshest
/// first. Memory MUST answer meaningfully without pgvector/embeddings per
/// the add-chat-memory contract — this ordering is that guarantee.
///
/// The <see cref="Rank"/> method accepts an optional
/// <c>outcomeBoost</c>: a per-scope integer the caller computes
/// (typically via <see cref="MemoryRankingOutcomeBoost"/> from
/// the project's <see cref="Domain.Learning.LearningCandidate"/>
/// counters) and that demotes or promotes a fact's effective
/// <c>CreatedAt</c> by the boost as a ticks offset. The boost is an
/// interim consumer of <see cref="CappedSignedContribution"/>; zero
/// preserves the standing-then-freshest contract.
/// </summary>
public static class MemoryFallbackRanking
{
    /// <summary>
    /// Ticks-per-second — the unit of a <see cref="DateTimeOffset"/>'s
    /// binary representation. The boost offsets a fact's
    /// <c>CreatedAt</c> by <c>outcomeBoost * TicksPerSecond</c>, which
    /// is monotonic with the boost and safe to compare across facts.
    /// </summary>
    public const long TicksPerSecond = TimeSpan.TicksPerSecond;

    /// <summary>Ranks visible facts: standing first, then freshest, with an optional outcome boost on the freshest order.</summary>
    /// <param name="facts">Visible facts of one query scope.</param>
    /// <param name="limit">Maximum entries returned.</param>
    /// <param name="outcomeBoost">
    /// Per-scope integer from <see cref="MemoryRankingOutcomeBoost"/>;
    /// positive values promote (newer) and negative values demote
    /// (older). The unit is seconds of <c>CreatedAt</c> offset. Zero
    /// leaves the ordering as standing-then-freshest.
    /// </param>
    public static IReadOnlyList<MemoryFactView> Rank(IEnumerable<MemoryFactView> facts, int limit, int outcomeBoost = 0)
    {
        // The boost is a fixed seconds-of-CreatedAt offset applied per
        // row; capture it once and freeze the lambda so the LINQ
        // sort key is allocation-free.
        var effectiveBoostTicks = outcomeBoost * TicksPerSecond;

        return [.. facts
            .OrderByDescending(static fact => fact.Kind == MemoryFactKind.Standing)
            .ThenByDescending(fact => fact.CreatedAt.AddTicks(effectiveBoostTicks))
            .Take(limit)];
    }
}
