using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Registry.Entries;
using EditionFeatureCatalog = Comuki.Shared.Editions.Features;
using EditionLimitCatalog = Comuki.Shared.Editions.Limits;

namespace Comuki.Shared.Editions.Registry;

/// <summary>
/// In-memory <see cref="IEditionCapabilityRegistry"/> over the static
/// <see cref="EditionFeatureCatalog"/> / <see cref="EditionLimitCatalog"/>
/// catalogs. The using-aliases exist ONLY to avoid a name collision
/// between this interface's <c>Features</c>/<c>Limits</c> PROPERTIES and
/// the catalogs' TYPE names in the same file — do not remove them.
/// </summary>
public sealed class EditionCapabilityRegistry : IEditionCapabilityRegistry
{
    private readonly Dictionary<string, Feature> featuresByKey;
    private readonly Dictionary<string, Limit> limitsByKey;

    public EditionCapabilityRegistry()
    {
        Features = EditionFeatureCatalog.All;
        Limits = EditionLimitCatalog.All;
        featuresByKey = Features.ToDictionary(static feature => feature.Key.Value, StringComparer.Ordinal);
        limitsByKey = Limits.ToDictionary(static limit => limit.Key.Value, StringComparer.Ordinal);

        Entries = [.. Features
            .Select(static feature => new RegistryEntry(feature.Key.Value, feature.Description, feature.MinimumRank, RegistryEntrySource.Feature))
            .Concat(Limits.Select(static limit => new RegistryEntry(limit.Key.Value, limit.Description, 0, RegistryEntrySource.Limit)))
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<Feature> Features { get; }

    /// <inheritdoc />
    public IReadOnlyList<Limit> Limits { get; }

    /// <inheritdoc />
    public IReadOnlyList<RegistryEntry> Entries { get; }

    /// <inheritdoc />
    public bool TryGetFeature(FeatureKey key, out Feature? feature) => featuresByKey.TryGetValue(key.Value, out feature);

    /// <inheritdoc />
    public bool TryGetLimit(LimitKey key, out Limit? limit) => limitsByKey.TryGetValue(key.Value, out limit);
}
