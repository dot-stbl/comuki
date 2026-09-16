namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>The consolidation worker's status payload for <c>GET /api/v1/workers/background</c>: <c>{ "promoted": 2, "decayed": 0, "total": 150 }</c>.</summary>
/// <param name="Promoted">Ephemeral facts promoted to standing this cycle.</param>
/// <param name="Decayed">Standing facts decayed to ephemeral this cycle.</param>
/// <param name="Total">Active (not superseded) facts after the cycle.</param>
public sealed record MemoryConsolidationCounters(int Promoted, int Decayed, int Total);
