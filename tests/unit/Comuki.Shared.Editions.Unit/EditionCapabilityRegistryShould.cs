using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Registry;
using Comuki.Shared.Editions.Registry.Entries;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// EditionCapabilityRegistry tests — the in-memory view over the static
/// Features / Limits catalogs every gate call site reads from.
/// </summary>
public sealed class EditionCapabilityRegistryShould
{
    [Fact(DisplayName = "Given the registry, when Features is read, then it equals Features.All (same entries, same order)")]
    public void FeaturesEqualsCatalog()
    {
        var registry = new EditionCapabilityRegistry();

        registry.Features.ShouldBe(Features.All);
    }

    [Fact(DisplayName = "Given the registry, when Limits is read, then it equals Limits.All (same entries, same order)")]
    public void LimitsEqualsCatalog()
    {
        var registry = new EditionCapabilityRegistry();

        registry.Limits.ShouldBe(Limits.All);
    }

    [Fact(DisplayName = "Given the registry, when Entries is read, then its count equals Features.All.Count + Limits.All.Count")]
    public void EntriesCountSumsFeaturesAndLimits()
    {
        var registry = new EditionCapabilityRegistry();

        registry.Entries.Count.ShouldBe(Features.All.Count + Limits.All.Count);
    }

    [Fact(DisplayName = "Given the registry, when Entries is read, then every Feature-kind entry carries the source feature's MinimumRank")]
    public void FeatureEntriesCarryMinimumRank()
    {
        var registry = new EditionCapabilityRegistry();

        var featureEntries = registry.Entries.Where(static entry => entry.Source == RegistryEntrySource.Feature).ToList();
        featureEntries.ShouldNotBeEmpty();

        foreach (var entry in featureEntries)
        {
            var source = Features.All.Single(feature => feature.Key.Value == entry.Key);
            entry.MinimumRank.ShouldBe(source.MinimumRank);
        }
    }

    [Fact(DisplayName = "Given the registry, when Entries is read, then every Limit-kind entry has MinimumRank == 0")]
    public void LimitEntriesAlwaysRankZero()
    {
        var registry = new EditionCapabilityRegistry();

        foreach (var entry in registry.Entries.Where(static entry => entry.Source == RegistryEntrySource.Limit))
        {
            entry.MinimumRank.ShouldBe(0);
        }
    }

    [Fact(DisplayName = "Given the registry, when Entries is read, then they are sorted ascending by Key (ordinal)")]
    public void EntriesSortedByKey()
    {
        var registry = new EditionCapabilityRegistry();

        registry.Entries
            .Select(static entry => entry.Key)
            .ShouldBe(registry.Entries.Select(static entry => entry.Key).OrderBy(static key => key, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Given the registry, when TryGetFeature runs with MultiRepo.Key, then it hits")]
    public void TryGetFeatureHits()
    {
        var registry = new EditionCapabilityRegistry();

        var hit = registry.TryGetFeature(Features.MultiRepo.Key, out var feature);

        hit.ShouldBeTrue();
        feature.ShouldNotBeNull();
        feature.Key.Value.ShouldBe("multi-repo");
    }

    [Fact(DisplayName = "Given the registry, when TryGetFeature runs with a non-existent key, then it misses")]
    public void TryGetFeatureMisses()
    {
        var registry = new EditionCapabilityRegistry();

        var hit = registry.TryGetFeature(FeatureKey.Parse("does-not-exist"), out var feature);

        hit.ShouldBeFalse();
        feature.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the registry, when TryGetLimit runs with Projects.Key, then it hits")]
    public void TryGetLimitHits()
    {
        var registry = new EditionCapabilityRegistry();

        var hit = registry.TryGetLimit(Limits.Projects.Key, out var limit);

        hit.ShouldBeTrue();
        limit.ShouldNotBeNull();
        limit.Key.Value.ShouldBe("projects");
    }

    [Fact(DisplayName = "Given the registry, when TryGetLimit runs with a non-existent key, then it misses")]
    public void TryGetLimitMisses()
    {
        var registry = new EditionCapabilityRegistry();
        var missing = LimitKey.Parse("does-not-exist");

        var hit = registry.TryGetLimit(missing, out var limit);

        hit.ShouldBeFalse();
        limit.ShouldBeNull();
    }
}
