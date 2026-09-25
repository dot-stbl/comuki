namespace Comuki.Shared.Editions.Registry.Entries;

/// <summary>
/// A uniform view over one <see cref="Catalog.Feature"/> or
/// <see cref="Catalog.Limit"/> entry, for enumeration consumers (the
/// generated capability table and the architecture tests — both
/// follow-up changes; this shape is built now so those changes don't
/// need to touch the registry). A <see cref="RegistryEntrySource.Limit"/>
/// entry always reports <c>MinimumRank = 0</c>: a limit applies at every
/// tier, only its numeric cap changes.
/// </summary>
/// <param name="Key">The feature/limit key.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="MinimumRank">Smallest rank at which the entry is gated; 0 for every <see cref="RegistryEntrySource.Limit"/>.</param>
/// <param name="Source">Which catalog this entry came from.</param>
public sealed record RegistryEntry(string Key, string Description, int MinimumRank, RegistryEntrySource Source);
