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
/// <remarks>
/// All instance state (the lookup dictionaries, the sorted entry list)
/// is built once at construction from the static catalogs via field
/// initializers — the empty primary ctor is the canonical sealed-class
/// shape (constructors-and-fields §1). No parameterized ctor is exposed
/// because the catalogs are the single source of truth.
/// </remarks>
public sealed class EditionCapabilityRegistry() : IEditionCapabilityRegistry
{
    private static readonly IReadOnlyList<Feature> featureCatalog = EditionFeatureCatalog.All;
    private static readonly IReadOnlyList<Limit> limitCatalog = EditionLimitCatalog.All;

    private static readonly Dictionary<string, Feature> featuresByKey = featureCatalog
        .ToDictionary(static feature => feature.Key.Value, StringComparer.Ordinal);

    private static readonly Dictionary<string, Limit> limitsByKey = limitCatalog
        .ToDictionary(static limit => limit.Key.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyList<RegistryEntry> entries = [.. featureCatalog
        .Select(static feature => new RegistryEntry(feature.Key.Value, feature.Description, feature.MinimumRank, RegistryEntrySource.Feature))
        .Concat(limitCatalog.Select(static limit => new RegistryEntry(limit.Key.Value, limit.Description, 0, RegistryEntrySource.Limit)))
        .OrderBy(static entry => entry.Key, StringComparer.Ordinal)];

    /// <inheritdoc />
    public IReadOnlyList<Feature> Features { get; } = featureCatalog;

    /// <inheritdoc />
    public IReadOnlyList<Limit> Limits { get; } = limitCatalog;

    /// <inheritdoc />
    public IReadOnlyList<RegistryEntry> Entries { get; } = entries;

    /// <inheritdoc />
    public bool TryGetFeature(FeatureKey key, out Feature? feature)
    {
        return featuresByKey.TryGetValue(key.Value, out feature);
    }

    /// <inheritdoc />
    public bool TryGetLimit(LimitKey key, out Limit? limit)
    {
        return limitsByKey.TryGetValue(key.Value, out limit);
    }
}
