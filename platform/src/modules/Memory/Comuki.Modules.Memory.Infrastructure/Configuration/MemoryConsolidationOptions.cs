using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Memory.Infrastructure.Configuration;

/// <summary>
/// Knobs of the memory "sleep cycle" — the consolidation worker that
/// promotes proven-useful ephemeral facts to standing and decays
/// long-unread standing facts back to ephemeral. Bound from
/// <c>Memory:Consolidation</c> when the host passes configuration into
/// <c>AddMemoryPersistence</c>; the defaults below are the human-sleep
/// analogy the design pinned: a fact needs 3 reads to stick, and three
/// months of silence to fade.
/// </summary>
public sealed class MemoryConsolidationOptions
{
    /// <summary>Configuration section name (used by the persistence installer).</summary>
    public const string SectionName = "Memory:Consolidation";

    /// <summary>How many reads before an ephemeral fact is promoted to standing.</summary>
    [Range(1, 100)]
    public int PromoteReadThreshold { get; init; } = 3;

    /// <summary>How long a standing fact can go unread before decaying to ephemeral.</summary>
    [Range(1, 365)]
    public int DecayDays { get; init; } = 90;

    /// <summary>How often the consolidation pass runs.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);
}
