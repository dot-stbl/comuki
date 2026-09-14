namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>Outcome of one <see cref="MemorySeeder.SeedAsync"/> pass.</summary>
/// <param name="Written">Facts inserted (topic key was absent).</param>
/// <param name="Superseded">Facts whose text changed — the previous row was superseded and a new row written.</param>
/// <param name="Unchanged">Facts skipped because the stored text already matches.</param>
public sealed record MemorySeedResult(int Written, int Superseded, int Unchanged);
