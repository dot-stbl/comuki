// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
using Comuki.Shared.Filtering.Unit.TestEntities;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Filtering.Unit;

/// <summary>
///     Sort and pagination behaviour: default sort, explicit sort, direction, unknown
///     field fallback, pagination bounds, and pagination boundary conditions.
/// </summary>
public sealed class FilterSortShould
{
    private static readonly IQueryable<SampleEntity> data = new List<SampleEntity>
    {
        new()
        {
            Name = "charlie", Age = 30, CreatedAt = new DateTimeOffset(2024,
                3,
                1,
                0,
                0,
                0,
                TimeSpan.Zero)
        },
        new()
        {
            Name = "alice", Age = 25, CreatedAt = new DateTimeOffset(2024,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero)
        },
        new()
        {
            Name = "bob", Age = 40, CreatedAt = new DateTimeOffset(2024,
                2,
                1,
                0,
                0,
                0,
                TimeSpan.Zero)
        }
    }.AsQueryable();

    /// <summary>Default sort (no <c>sort</c> param) is stable on first registered field.</summary>
    [Fact]
    public void ApplyDefaultSortWhenSpecIsNull()
    {
        var sorted = data.ApplySort(null).ToList();
        sorted.ShouldNotBeEmpty();
    }

    /// <summary>Sort ascending by Name.</summary>
    [Fact]
    public void SortAscendingByName()
    {
        var sorted = data.ApplySort("Name,asc").ToList();

        sorted[0].Name.ShouldBe("alice");
        sorted[1].Name.ShouldBe("bob");
        sorted[2].Name.ShouldBe("charlie");
    }

    /// <summary>Sort descending by Name.</summary>
    [Fact]
    public void SortDescendingByName()
    {
        var sorted = data.ApplySort("Name,desc").ToList();

        sorted[0].Name.ShouldBe("charlie");
        sorted[1].Name.ShouldBe("bob");
        sorted[2].Name.ShouldBe("alice");
    }

    /// <summary>Sort without direction defaults to asc.</summary>
    [Fact]
    public void SortWithoutDirectionDefaultsToAsc()
    {
        var sorted = data.ApplySort("Name").ToList();

        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Sort direction is case-insensitive.</summary>
    [Fact]
    public void SortDirectionIsCaseInsensitive()
    {
        var sortedAsc = data.ApplySort("Name,ASC").ToList();
        var sortedDesc = data.ApplySort("Name,DESC").ToList();

        sortedAsc[0].Name.ShouldBe("alice");
        sortedDesc[0].Name.ShouldBe("charlie");
    }

    /// <summary>Sort by numeric field.</summary>
    [Fact]
    public void SortByNumericField()
    {
        var asc = data.ApplySort("Age,asc").ToList();
        var desc = data.ApplySort("Age,desc").ToList();

        asc[0].Age.ShouldBe(25);
        desc[0].Age.ShouldBe(40);
    }

    /// <summary>Sort by date field.</summary>
    [Fact]
    public void SortByDateField()
    {
        var asc = data.ApplySort("CreatedAt,asc").ToList();
        var desc = data.ApplySort("CreatedAt,desc").ToList();

        asc[0].Name.ShouldBe("alice"); // 2024-01
        desc[0].Name.ShouldBe("charlie"); // 2024-03
    }

    /// <summary>Sort field case-insensitive.</summary>
    [Fact]
    public void SortFieldCaseInsensitive()
    {
        var lower = data.ApplySort("name,asc").ToList();
        var upper = data.ApplySort("NAME,asc").ToList();

        lower[0].Name.ShouldBe("alice");
        upper[0].Name.ShouldBe("alice");
    }

    /// <summary>Sort on unknown field falls back to default (no throw — stale-client safe).</summary>
    [Fact]
    public void SortUnknownFieldFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort("nonexistent,asc").ToList());
    }

    /// <summary>Sort on <c>[NotMapped]</c> field falls back to default.</summary>
    [Fact]
    public void SortNotMappedFieldFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort("SecretKey,asc").ToList());
    }

    /// <summary>Whitespace-only sort spec falls back to default.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SortEmptySpecFallsBackToDefault(string? sort)
    {
        Should.NotThrow(() => data.ApplySort(sort).ToList());
    }

    /// <summary>Sort spec with extra commas ignores them.</summary>
    [Fact]
    public void SortExtraCommasIgnored()
    {
        // The sort parser only takes parts[0] and parts[1]. Extra commas are dropped
        // by Split with TrimEntries + RemoveEmptyEntries.
        Should.NotThrow(static () => data.ApplySort("Name,asc,extra,comma").ToList());
    }

    /// <summary>Sort empty direction string treats as asc.</summary>
    [Fact]
    public void SortEmptyDirectionIsAsc()
    {
        var sorted = data.ApplySort("Name,").ToList();
        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Sort unknown direction string (not "asc"/"desc") treats as asc.</summary>
    [Fact]
    public void SortUnknownDirectionIsAsc()
    {
        var sorted = data.ApplySort("Name,sideways").ToList();
        sorted[0].Name.ShouldBe("alice");
    }

    // ── Multi-field sort ────────────────────────────────────────────────────────

    /// <summary>
    ///     Multi-field sort: primary sort applies, secondary breaks ties.
    ///     <c>Age,asc;Name,asc</c> — Alice (25) before Bob (40) before Charlie (30
    ///     would come last) — actually with Age asc: Alice(25), Charlie(30), Bob(40).
    /// </summary>
    [Fact]
    public void SortTwoFieldsAscAsc()
    {
        var sorted = data.ApplySort("Age,asc;Name,asc").ToList();

        sorted[0].Name.ShouldBe("alice"); // 25
        sorted[1].Name.ShouldBe("charlie"); // 30
        sorted[2].Name.ShouldBe("bob"); // 40
    }

    /// <summary>
    ///     Multi-field sort with secondary descending — secondary breaks ties in
    ///     the opposite direction. Tied Ages would be ordered by Name desc.
    /// </summary>
    [Fact]
    public void SortTwoFieldsAscDesc()
    {
        // Add a tie-breaker so the secondary sort actually fires.
        var data = new List<SampleEntity>
        {
            new()
            {
                Name = "charlie", Age = 25, CreatedAt = new DateTimeOffset(2024,
                    3,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero)
            },
            new()
            {
                Name = "alice", Age = 25, CreatedAt = new DateTimeOffset(2024,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero)
            },
            new()
            {
                Name = "bob", Age = 40, CreatedAt = new DateTimeOffset(2024,
                    2,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero)
            }
        }.AsQueryable();

        var sorted = data.ApplySort("Age,asc;CreatedAt,desc").ToList();

        sorted[0].Name.ShouldBe("charlie"); // 25, latest CreatedAt → wins tie
        sorted[1].Name.ShouldBe("alice"); // 25, earlier CreatedAt → loses tie
        sorted[2].Name.ShouldBe("bob"); // 40
    }

    /// <summary>Multi-field sort with three criteria.</summary>
    [Fact]
    public void SortThreeFields()
    {
        var sorted = data.ApplySort("Age,asc;Name,asc").ToList();

        sorted.Count.ShouldBe(3);
        sorted[0].Age.ShouldBe(25);
        sorted[2].Age.ShouldBe(40);
    }

    /// <summary>
    ///     Multi-field sort with an unknown secondary field silently drops the
    ///     secondary criterion (the primary still applies) — stale-client safe.
    /// </summary>
    [Fact]
    public void SortWithUnknownSecondarySkipsSecondary()
    {
        var sorted = data.ApplySort("Age,asc;nonexistent,asc").ToList();

        sorted.Count.ShouldBe(3);
        sorted[0].Age.ShouldBe(25); // alice
        sorted[1].Age.ShouldBe(30); // charlie
        sorted[2].Age.ShouldBe(40); // bob
    }

    /// <summary>
    ///     Multi-field sort where the primary field is unknown falls back to the
    ///     default sort entirely.
    /// </summary>
    [Fact]
    public void SortWithUnknownPrimaryFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort("nonexistent,asc;Age,asc").ToList());
    }

    /// <summary>Multi-field sort handles extra semicolons gracefully.</summary>
    [Fact]
    public void SortTrailingSemicolonIgnored()
    {
        var sorted = data.ApplySort("Name,asc;").ToList();
        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Multi-field sort handles leading semicolon gracefully.</summary>
    [Fact]
    public void SortLeadingSemicolonIgnored()
    {
        var sorted = data.ApplySort(";Name,asc").ToList();
        sorted[0].Name.ShouldBe("alice");
    }

    // ── Multi-field sort adversarial ──────────────────────────────────────────────

    /// <summary>Sort spec of only commas falls back to default (no criterion survives).</summary>
    [Fact]
    public void SortOnlyCommasFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort(",,,").ToList());
    }

    /// <summary>Sort spec of only semicolons falls back to default.</summary>
    [Fact]
    public void SortOnlySemicolonsFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort(";;;").ToList());
    }

    /// <summary>Sort spec of only whitespace falls back to default.</summary>
    [Fact]
    public void SortOnlyWhitespaceFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort("   ").ToList());
    }

    /// <summary>Trailing comma after direction is treated as asc.</summary>
    [Fact]
    public void SortTrailingCommaAfterDirectionFallsBackToAsc()
    {
        var sorted = data.ApplySort("Name,asc,").ToList();
        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Same field twice applies both orders — primary then secondary.</summary>
    [Fact]
    public void SortSameFieldTwiceAppliesBothOrders()
    {
        // Two criteria on Age — primary asc, secondary desc. With unique Ages in data,
        // secondary never fires but the syntax is valid.
        var sorted = data.ApplySort("Age,asc;Age,desc").ToList();

        sorted[0].Age.ShouldBe(25); // alice
        sorted[2].Age.ShouldBe(40); // bob
    }

    /// <summary>Sort criteria with only direction (no field) are dropped.</summary>
    [Fact]
    public void SortDirectionOnlyCriterionIsDropped()
    {
        // First criterion ",asc" has no field — dropped. Second "Name,asc" applies.
        var sorted = data.ApplySort(",asc;Name,asc").ToList();

        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Many criteria (50) all apply as OrderBy + 49 ThenBys.</summary>
    [Fact]
    public void SortManyCriteria()
    {
        // All criteria reference the same field — primary fires, others are no-ops
        // on unique values. Tests that the loop handles many criteria.
        var criteria = Enumerable.Range(0, 50).Select(_ => "Age,asc");
        var spec = string.Join(';', criteria);

        Should.NotThrow(() => data.ApplySort(spec).ToList());
    }

    /// <summary>Mixed-case directions across multiple criteria work.</summary>
    [Fact]
    public void SortMixedCaseDirections()
    {
        var sorted = data.ApplySort("Age,ASC;Name,DESC").ToList();

        sorted[0].Age.ShouldBe(25); // alice (25, ASC)
        sorted[2].Age.ShouldBe(40); // bob (40)
    }

    /// <summary>Sort by numeric field primary, string field secondary.</summary>
    [Fact]
    public void SortByNumberPrimaryStringSecondary()
    {
        // Add a tie-breaker scenario: two entities with same Age, different Name.
        var data = new List<SampleEntity>
        {
            new() { Name = "charlie", Age = 25 },
            new() { Name = "alice", Age = 25 },
            new() { Name = "bob", Age = 40 }
        }.AsQueryable();

        var sorted = data.ApplySort("Age,asc;Name,asc").ToList();

        sorted[0].Name.ShouldBe("alice"); // 25, "alice" wins Name asc
        sorted[1].Name.ShouldBe("charlie"); // 25, "charlie"
        sorted[2].Name.ShouldBe("bob"); // 40
    }

    /// <summary>
    ///     Sort with a typo on the first criterion (unknown field) still works
    ///     if a subsequent criterion resolves.
    /// </summary>
    [Fact]
    public void SortUnknownPrimaryKnownSecondaryStillFallsBack()
    {
        // First criterion's field is unknown — but our parser SKIPS unknowns
        // within a multi-field spec, so the second criterion (Name,asc) applies.
        var sorted = data.ApplySort("nonexistent,asc;Name,asc").ToList();

        sorted[0].Name.ShouldBe("alice");
    }

    /// <summary>Sort where ALL criteria fields are unknown falls back to default.</summary>
    [Fact]
    public void SortAllUnknownCriteriaFallsBackToDefault()
    {
        Should.NotThrow(static () => data.ApplySort("foo,asc;bar,desc;baz,asc").ToList());
    }
}

/// <summary>Pagination bound checks on <see cref="FilterQuery" />.</summary>
public sealed class FilterQueryNormalizationShould
{
    /// <summary>Default page is 1.</summary>
    [Fact]
    public void DefaultPageIsOne()
    {
        new FilterQuery().Page.ShouldBe(1);
    }

    /// <summary>Default page size is 25.</summary>
    [Fact]
    public void DefaultPageSizeIsTwentyFive()
    {
        new FilterQuery().PageSize.ShouldBe(25);
    }

    /// <summary>Page 0 normalizes to 1.</summary>
    [Fact]
    public void NormalizePageZeroToOne()
    {
        new FilterQuery { Page = 0 }.Normalized().Page.ShouldBe(1);
    }

    /// <summary>Negative page normalizes to 1.</summary>
    [Fact]
    public void NormalizeNegativePageToOne()
    {
        new FilterQuery { Page = -5 }.Normalized().Page.ShouldBe(1);
    }

    /// <summary>Page 1 stays 1.</summary>
    [Fact]
    public void KeepValidPage()
    {
        new FilterQuery { Page = 1 }.Normalized().Page.ShouldBe(1);
    }

    /// <summary>PageSize 0 clamps to 1.</summary>
    [Fact]
    public void ClampPageSizeZeroToOne()
    {
        new FilterQuery { PageSize = 0 }.Normalized().PageSize.ShouldBe(1);
    }

    /// <summary>PageSize negative clamps to 1.</summary>
    [Fact]
    public void ClampNegativePageSizeToOne()
    {
        new FilterQuery { PageSize = -100 }.Normalized().PageSize.ShouldBe(1);
    }

    /// <summary>PageSize above 100 clamps to 100.</summary>
    [Fact]
    public void ClampPageSizeAbove100()
    {
        new FilterQuery { PageSize = 1000 }.Normalized().PageSize.ShouldBe(100);
    }

    /// <summary>PageSize exactly 100 is allowed.</summary>
    [Fact]
    public void AllowPageSize100()
    {
        new FilterQuery { PageSize = 100 }.Normalized().PageSize.ShouldBe(100);
    }

    /// <summary>PageSize exactly 1 is allowed.</summary>
    [Fact]
    public void AllowPageSize1()
    {
        new FilterQuery { PageSize = 1 }.Normalized().PageSize.ShouldBe(1);
    }

    /// <summary>Skip is zero on page 1.</summary>
    [Fact]
    public void SkipZeroOnFirstPage()
    {
        new FilterQuery { Page = 1, PageSize = 25 }.Skip().ShouldBe(0);
    }

    /// <summary>Skip is correct on page 3 with size 25 (offset 50).</summary>
    [Fact]
    public void SkipOnLaterPage()
    {
        new FilterQuery { Page = 3, PageSize = 25 }.Skip().ShouldBe(50);
    }

    /// <summary>Skip uses normalized page (page 0 → skip 0, not -25).</summary>
    [Fact]
    public void SkipUsesNormalizedPage()
    {
        new FilterQuery { Page = 0, PageSize = 25 }.Normalized().Skip().ShouldBe(0);
    }

    /// <summary>Filter and Sort pass through Normalized unchanged.</summary>
    [Fact]
    public void PassFilterAndSortThroughNormalized()
    {
        var query = new FilterQuery { Filter = "name~x", Sort = "name,desc" };
        var normalized = query.Normalized();

        normalized.Filter.ShouldBe("name~x");
        normalized.Sort.ShouldBe("name,desc");
    }
}
