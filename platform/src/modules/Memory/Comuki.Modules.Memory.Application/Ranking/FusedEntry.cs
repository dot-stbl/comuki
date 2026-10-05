using Comuki.Modules.Memory.Application.Views;

namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// One entry in the Reciprocal Rank Fusion working map: the candidate
/// <see cref="MemoryFactView"/> paired with the running RRF total from
/// the per-list contributions seen so far. The final fused score rewrites
/// <see cref="MemoryFactView.FusedScore"/> on the way out; the entry
/// here is the internal accumulator.
/// </summary>
/// <param name="View">The candidate fact row.</param>
/// <param name="Score">The cumulative RRF score from the lists already processed.</param>
public sealed record FusedEntry(MemoryFactView View, float Score);
