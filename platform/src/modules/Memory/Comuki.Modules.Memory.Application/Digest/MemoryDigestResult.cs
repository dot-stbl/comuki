namespace Comuki.Modules.Memory.Application.Digest;

/// <summary>
/// The compact brain-context digest: top-5 relevant facts plus the 5
/// freshest standing ones (deduplicated against the relevant list).
/// Callers journal this output — what was fed to the brain is auditable,
/// the digest itself holds no journal.
/// </summary>
/// <param name="Relevant">Top relevant facts for the task (fallback-ranked for now).</param>
/// <param name="FreshestStanding">Freshest standing facts, excluding <see cref="Relevant"/> entries.</param>
public sealed record MemoryDigestResult(
    IReadOnlyList<MemoryDigestEntry> Relevant,
    IReadOnlyList<MemoryDigestEntry> FreshestStanding);
