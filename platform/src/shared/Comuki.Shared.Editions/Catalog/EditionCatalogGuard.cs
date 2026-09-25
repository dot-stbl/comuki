namespace Comuki.Shared.Editions.Catalog;

/// <summary>
/// Shared duplicate-key guard for the <see cref="Features"/> and
/// <see cref="Limits"/> catalogs: fails fast (at static-init
/// time) when two entries share a key, and returns the entries sorted by
/// key — the shape <see cref="Registry.IEditionCapabilityRegistry"/>
/// exposes.
/// </summary>
internal static class EditionCatalogGuard
{
    /// <summary>Validates uniqueness of <paramref name="keySelector"/> across <paramref name="entries"/> and returns them sorted by key.</summary>
    /// <exception cref="InvalidOperationException">Two entries share a key.</exception>
    public static IReadOnlyList<T> EnsureUniqueSortedByKey<T>(IReadOnlyList<T> entries, Func<T, string> keySelector, string catalogName)
    {
        var duplicate = entries
            .GroupBy(keySelector, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);

        return duplicate is not null
            ? throw new InvalidOperationException($"Duplicate key '{duplicate.Key}' in the {catalogName} catalog.")
            : [.. entries.OrderBy(keySelector, StringComparer.Ordinal)];
    }
}
