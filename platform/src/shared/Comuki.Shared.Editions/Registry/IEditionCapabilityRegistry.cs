using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Registry.Entries;

namespace Comuki.Shared.Editions.Registry;

/// <summary>
/// The enumerable view over the <see cref="Editions.Features"/> /
/// <see cref="Editions.Limits"/> catalogs (issue #164 E3) — the single
/// source of truth every gate call site, the generated capability table,
/// and the architecture tests read (the latter two land in follow-up
/// changes; this port is built now so they don't need a new one).
/// </summary>
public interface IEditionCapabilityRegistry
{
    /// <summary>Every declared feature, sorted by key.</summary>
    public IReadOnlyList<Feature> Features { get; }

    /// <summary>Every declared limit, sorted by key.</summary>
    public IReadOnlyList<Limit> Limits { get; }

    /// <summary><see cref="Features"/> and <see cref="Limits"/> merged into one uniform, key-sorted view.</summary>
    public IReadOnlyList<RegistryEntry> Entries { get; }

    /// <summary>Looks up a feature by key.</summary>
    public bool TryGetFeature(FeatureKey key, out Feature? feature);

    /// <summary>Looks up a limit by key.</summary>
    public bool TryGetLimit(LimitKey key, out Limit? limit);
}
