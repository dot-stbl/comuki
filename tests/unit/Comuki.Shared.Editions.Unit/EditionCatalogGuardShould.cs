using Comuki.Shared.Editions.Catalog;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// EditionCatalogGuard tests — the internal duplicate-key guard the
/// Features / Limits catalogs run at static-init time. Reachable through
/// InternalsVisibleTo.
/// </summary>
public sealed class EditionCatalogGuardShould
{
    private sealed record FakeEntry(string Key);

    [Fact(DisplayName = "Given distinct keys, when EnsureUniqueSortedByKey runs, then the entries come back sorted ascending by key (ordinal)")]
    public void SortsDistinctEntriesByKey()
    {
        var entries = new List<FakeEntry> { new("c"), new("a"), new("b") };

        var result = EditionCatalogGuard.EnsureUniqueSortedByKey(
            entries,
            static entry => entry.Key,
            catalogName: "Fake");

        result.Select(static entry => entry.Key).ShouldBe(["a", "b", "c"]);
    }

    [Fact(DisplayName = "Given two entries sharing a key, when EnsureUniqueSortedByKey runs, then InvalidOperationException is thrown and its message names the duplicate key")]
    public void ThrowsOnDuplicateKey()
    {
        var entries = new List<FakeEntry> { new("a"), new("a") };

        var exception = Should.Throw<InvalidOperationException>(
            () => EditionCatalogGuard.EnsureUniqueSortedByKey(
                entries,
                static entry => entry.Key,
                catalogName: "Fake"));

        exception.Message.ShouldContain("'a'");
        exception.Message.ShouldContain("Fake");
    }
}
