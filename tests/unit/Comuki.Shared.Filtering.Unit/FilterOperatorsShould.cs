// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
using Comuki.Shared.Filtering.Translator;
using Comuki.Shared.Filtering.Unit.TestEntities;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Filtering.Unit;

/// <summary>
///     Positive-path tests: each operator does the right thing against in-memory data.
/// </summary>
public sealed class FilterOperatorsShould
{
    private static readonly DateTimeOffset referenceDate = new(2024,
        6,
        1,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static readonly IQueryable<SampleEntity> data = new List<SampleEntity>
    {
        new()
        {
            Name = "alice", Email = "alice@example.com", Status = SampleStatus.Active,
            Age = 25, Score = 100L, Balance = 50.5m, Rating = 4.5, IsActive = true,
            CreatedAt = referenceDate, Id = new Guid(0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x1) /* 00000000-0000-0000-0000-000000000001 */
        },
        new()
        {
            Name = "Bob", Email = "bob@example.com", Status = SampleStatus.Inactive,
            Age = 30, Score = 50L, Balance = 0m, Rating = 2.0, IsActive = false,
            CreatedAt = referenceDate.AddDays(1), Id = new Guid(0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x2) /* 00000000-0000-0000-0000-000000000002 */
        },
        new()
        {
            Name = "Carol", Email = "carol@other.io", Status = SampleStatus.Archived,
            Age = 40, Score = 250L, Balance = 999.99m, Rating = 5.0, IsActive = false,
            CreatedAt = referenceDate.AddDays(-1), Id = new Guid(0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x0,
                0x3) /* 00000000-0000-0000-0000-000000000003 */
        }
    }.AsQueryable();

    private static List<SampleEntity> Run(string filter)
    {
        return [.. data.Where(FilterExpression.ParseFor<SampleEntity>(filter)!)];
    }

    // ── Eq / NotEq ───────────────────────────────────────────────────────────────

    /// <summary>Eq matches exactly one value.</summary>
    [Fact(DisplayName = "When eq Matches Exact Value, then test passes")]
    public void EqMatchesExactValue()
    {
        var result = Run("Name==alice");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>NotEq excludes the value, returns the rest.</summary>
    [Fact(DisplayName = "When not Eq Excludes Value, then test passes")]
    public void NotEqExcludesValue()
    {
        var result = Run("Name!=alice");
        result.Count.ShouldBe(2);
        result.ShouldNotContain(static e => e.Name == "alice");
    }

    // ── String operators ─────────────────────────────────────────────────────────

    /// <summary>Contains matches substrings.</summary>
    [Fact(DisplayName = "When contains Matches Substring, then test passes")]
    public void ContainsMatchesSubstring()
    {
        // "example" is in alice + Bob emails, not in Carol's
        var result = Run("Email~example");
        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Bob");
    }

    /// <summary>StartsWith matches prefixes.</summary>
    [Fact(DisplayName = "When starts With Matches Prefix, then test passes")]
    public void StartsWithMatchesPrefix()
    {
        var result = Run("Email^=alice");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>EndsWith matches suffixes.</summary>
    [Fact(DisplayName = "When ends With Matches Suffix, then test passes")]
    public void EndsWithMatchesSuffix()
    {
        var result = Run("Email$=io");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Carol");
    }

    // ── Range operators (numbers) ────────────────────────────────────────────────

    /// <summary>Gt strictly greater than.</summary>
    [Fact(DisplayName = "When gt Matches Strictly Greater, then test passes")]
    public void GtMatchesStrictlyGreater()
    {
        var result = Run("Age>25");
        result.Count.ShouldBe(2);
        result.ShouldNotContain(static e => e.Name == "alice");
    }

    /// <summary>Gte includes boundary.</summary>
    [Fact(DisplayName = "When gte Includes Boundary, then test passes")]
    public void GteIncludesBoundary()
    {
        var result = Run("Age>=25");
        result.Count.ShouldBe(3);
    }

    /// <summary>Lt strictly less than.</summary>
    [Fact(DisplayName = "When lt Matches Strictly Less, then test passes")]
    public void LtMatchesStrictlyLess()
    {
        var result = Run("Score<100");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Bob");
    }

    /// <summary>Lte includes boundary.</summary>
    [Fact(DisplayName = "When lte Includes Boundary, then test passes")]
    public void LteIncludesBoundary()
    {
        var result = Run("Score<=100");
        result.Count.ShouldBe(2);
    }

    /// <summary>Decimal range works.</summary>
    [Fact(DisplayName = "When decimal Range Works, then test passes")]
    public void DecimalRangeWorks()
    {
        var result = Run("Balance>50");
        result.Count.ShouldBe(2);
    }

    /// <summary>Double range works.</summary>
    [Fact(DisplayName = "When double Range Works, then test passes")]
    public void DoubleRangeWorks()
    {
        var result = Run("Rating>=4.5");
        result.Count.ShouldBe(2);
    }

    // ── Range operators (dates) ──────────────────────────────────────────────────

    /// <summary>Date Gt matches strictly after.</summary>
    [Fact(DisplayName = "When date Gt Matches Strictly After, then test passes")]
    public void DateGtMatchesStrictlyAfter()
    {
        var result = Run($"CreatedAt>{referenceDate:O}");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Bob");
    }

    /// <summary>Date Gte includes boundary.</summary>
    [Fact(DisplayName = "When date Gte Includes Boundary, then test passes")]
    public void DateGteIncludesBoundary()
    {
        var result = Run($"CreatedAt>={referenceDate:O}");
        result.Count.ShouldBe(2);
    }

    /// <summary>Date Lt matches strictly before.</summary>
    [Fact(DisplayName = "When date Lt Matches Strictly Before, then test passes")]
    public void DateLtMatchesStrictlyBefore()
    {
        var result = Run($"CreatedAt<{referenceDate:O}");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Carol");
    }

    // ── In operator ──────────────────────────────────────────────────────────────

    /// <summary>In matches any value in the list.</summary>
    [Fact(DisplayName = "When in Matches Any Listed Value, then test passes")]
    public void InMatchesAnyListedValue()
    {
        var result = Run("Status[]=Active,Archived");
        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Carol");
    }

    /// <summary>In on Guid.</summary>
    [Fact(DisplayName = "When in On Guid, then test passes")]
    public void InOnGuid()
    {
        var result = Run("Id[]=00000000-0000-0000-0000-000000000001,00000000-0000-0000-0000-000000000002");
        result.Count.ShouldBe(2);
    }

    /// <summary>In with a single value behaves like Eq.</summary>
    [Fact(DisplayName = "When in With Single Value Behaves Like Eq, then test passes")]
    public void InWithSingleValueBehavesLikeEq()
    {
        var result = Run("Status[]=Active");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    // ── Bool Eq ──────────────────────────────────────────────────────────────────

    /// <summary>Bool Eq with true.</summary>
    [Fact(DisplayName = "When bool Eq True, then test passes")]
    public void BoolEqTrue()
    {
        var result = Run("IsActive==true");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>Bool Eq with false.</summary>
    [Fact(DisplayName = "When bool Eq False, then test passes")]
    public void BoolEqFalse()
    {
        var result = Run("IsActive==false");
        result.Count.ShouldBe(2);
    }

    // ── Combinations ─────────────────────────────────────────────────────────────

    /// <summary>AND of two conditions.</summary>
    [Fact(DisplayName = "When and Combines Two Conditions, then test passes")]
    public void AndCombinesTwoConditions()
    {
        var result = Run("Status==Active;Age==25");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>OR of two conditions.</summary>
    [Fact(DisplayName = "When or Combines Two Conditions, then test passes")]
    public void OrCombinesTwoConditions()
    {
        var result = Run("Status==Active|Status==Archived");
        result.Count.ShouldBe(2);
    }

    /// <summary>Nested AND inside OR.</summary>
    [Fact(DisplayName = "When nested And Inside Or, then test passes")]
    public void NestedAndInsideOr()
    {
        // (Status==Active ; Age==25) | (Status==Archived)
        var result = Run("(Status==Active;Age==25)|Status==Archived");
        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Carol");
    }

    /// <summary>Three-way AND.</summary>
    [Fact(DisplayName = "When three Way And, then test passes")]
    public void ThreeWayAnd()
    {
        var result = Run("Status==Active;Age==25;IsActive==true");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>AND of conditions where one matches none → empty result.</summary>
    [Fact(DisplayName = "When and With No Matches Returns Empty, then test passes")]
    public void AndWithNoMatchesReturnsEmpty()
    {
        var result = Run("Status==Active;Age==999");
        result.ShouldBeEmpty();
    }

    /// <summary>OR where one side is always false still returns the other.</summary>
    [Fact(DisplayName = "When or With False Side Returns True Side, then test passes")]
    public void OrWithFalseSideReturnsTrueSide()
    {
        var result = Run("Status==Active|Age==999");
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    // ── IsNull / IsNotNull ──────────────────────────────────────────────────────

    /// <summary><c>?</c> matches entities where a nullable reference field is null.</summary>
    [Fact(DisplayName = "When is Null Matches Nullable Reference Field, then test passes")]
    public void IsNullMatchesNullableReferenceField()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "alice", Nickname = null },
            new() { Name = "Bob", Nickname = "Bobby" },
            new() { Name = "Carol", Nickname = null }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Nickname?")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Carol");
    }

    /// <summary><c>!?</c> matches entities where a nullable reference field has a value.</summary>
    [Fact(DisplayName = "When is Not Null Matches Populated Reference Field, then test passes")]
    public void IsNotNullMatchesPopulatedReferenceField()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "alice", Nickname = null },
            new() { Name = "Bob", Nickname = "Bobby" }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Nickname!?")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Bob");
    }

    /// <summary><c>?</c> on a <see cref="Nullable{T}" /> value-type field works.</summary>
    [Fact(DisplayName = "When is Null Matches Nullable Value Type Field, then test passes")]
    public void IsNullMatchesNullableValueTypeField()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "alice", OptionalAge = null },
            new() { Name = "Bob", OptionalAge = 30 },
            new() { Name = "Carol", OptionalAge = null }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("OptionalAge?")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Carol");
    }

    /// <summary>
    ///     <c>!?</c> on a <see cref="Nullable{T}" /> value-type field works.
    /// </summary>
    [Fact(DisplayName = "When is Not Null Matches Populated Nullable Value Type Field, then test passes")]
    public void IsNotNullMatchesPopulatedNullableValueTypeField()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "alice", OptionalAge = null },
            new() { Name = "Bob", OptionalAge = 30 }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("OptionalAge!?")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("Bob");
    }

    /// <summary>IsNull combines with AND normally.</summary>
    [Fact(DisplayName = "When is Null And Other Condition, then test passes")]
    public void IsNullAndOtherCondition()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "alice", Nickname = null, Status = SampleStatus.Active },
            new() { Name = "Bob", Nickname = "Bobby", Status = SampleStatus.Active },
            new() { Name = "Carol", Nickname = null, Status = SampleStatus.Inactive }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Nickname?;Status==Active")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    // ── now(offset) function call ────────────────────────────────────────────────

    /// <summary>
    ///     <c>now(-7d)</c> is evaluated to <c>UtcNow - 7 days</c>. Entities created
    ///     within that window are matched. Uses relative timestamps so the test
    ///     does not depend on wall-clock time.
    /// </summary>
    [Fact(DisplayName = "When now Minus Seven Days Includes Only Recent Entities, then test passes")]
    public void NowMinusSevenDaysIncludesOnlyRecentEntities()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "fresh", CreatedAt = now.AddHours(-1) },
            new() { Name = "today", CreatedAt = now.AddDays(-2) },
            new() { Name = "weekOld", CreatedAt = now.AddDays(-6) },
            new() { Name = "old", CreatedAt = now.AddDays(-30) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("CreatedAt>=now(-7d)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(3);
        result.ShouldContain(static e => e.Name == "fresh");
        result.ShouldContain(static e => e.Name == "today");
        result.ShouldContain(static e => e.Name == "weekOld");
    }

    /// <summary><c>now(-1h)</c> with hours unit works.</summary>
    [Fact(DisplayName = "When now Minus One Hour Matches Recent, then test passes")]
    public void NowMinusOneHourMatchesRecent()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "minutesAgo", CreatedAt = now.AddMinutes(-15) },
            new() { Name = "hoursAgo", CreatedAt = now.AddHours(-2) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("CreatedAt>=now(-1h)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("minutesAgo");
    }

    /// <summary><c>now(-30m)</c> with minutes unit works.</summary>
    [Fact(DisplayName = "When now Minus Thirty Minutes, then test passes")]
    public void NowMinusThirtyMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "recent", CreatedAt = now.AddMinutes(-5) },
            new() { Name = "stale", CreatedAt = now.AddMinutes(-60) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("CreatedAt>=now(-30m)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("recent");
    }

    /// <summary><c>now(-1w)</c> with weeks unit works (7 days).</summary>
    [Fact(DisplayName = "When now Minus One Week, then test passes")]
    public void NowMinusOneWeek()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "daysOld", CreatedAt = now.AddDays(-3) },
            new() { Name = "weeksOld", CreatedAt = now.AddDays(-10) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("CreatedAt>=now(-1w)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("daysOld");
    }

    /// <summary>Positive offset (future) works.</summary>
    [Fact(DisplayName = "When now With Positive Offset Works, then test passes")]
    public void NowWithPositiveOffsetWorks()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "past", CreatedAt = now.AddHours(-1) },
            new() { Name = "future", CreatedAt = now.AddHours(2) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("CreatedAt<=now(1h)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("past");
    }

    /// <summary><c>now()</c> works on <see cref="DateTime" /> fields too.</summary>
    [Fact(DisplayName = "When now Works On Date Time Fields, then test passes")]
    public void NowWorksOnDateTimeFields()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "fresh", UpdatedAt = now.UtcDateTime.AddMinutes(-10) },
            new() { Name = "old", UpdatedAt = now.UtcDateTime.AddDays(-10) }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("UpdatedAt>=now(-1d)")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("fresh");
    }

    /// <summary>Function name is case-insensitive — <c>NOW()</c>, <c>Now()</c> work too.</summary>
    [Theory]
    [InlineData("NOW(-1d)")]
    [InlineData("Now(-1d)")]
    [InlineData("nOw(-1d)")]
    public void NowFunctionNameIsCaseInsensitive(string functionCall)
    {
        var now = DateTimeOffset.UtcNow;
        var data = new List<SampleEntity>
        {
            new() { Name = "fresh", CreatedAt = now.AddHours(-1) }
        }.AsQueryable();

        var filter = $"CreatedAt>={functionCall}";
        var predicate = FilterExpression.ParseFor<SampleEntity>(filter)!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------------
    // Case-insensitive string ops (PG-style symbols: ~*, ^=, $=)
    // ---------------------------------------------------------------------

    /// <summary><c>IContains</c> matches substrings regardless of case.</summary>
    [Theory]
    [InlineData("Banner", "banner", 1)]
    [InlineData("BANNER", "ban", 1)]
    [InlineData("banner", "xyz", 0)]
    public void IContainsMatchesCaseInsensitive(string fieldValue, string needle, int expectedCount)
    {
        var data = new List<SampleEntity> { new() { Name = fieldValue } }.AsQueryable();
        var predicate = FilterExpression.ParseFor<SampleEntity>($"Name~*{needle}")!;
        data.Where(predicate).Count().ShouldBe(expectedCount);
    }

    /// <summary><c>IStartsWith</c> matches prefixes regardless of case.</summary>
    [Theory]
    [InlineData("Admin", "adm", 1)]
    [InlineData("ADMIN", "Adm", 1)]
    [InlineData("Admin", "min", 0)]
    public void IStartsWithMatchesCaseInsensitivePrefix(string fieldValue, string prefix, int expectedCount)
    {
        var data = new List<SampleEntity> { new() { Email = fieldValue } }.AsQueryable();
        var predicate = FilterExpression.ParseFor<SampleEntity>($"Email^=*{prefix}")!;
        data.Where(predicate).Count().ShouldBe(expectedCount);
    }

    /// <summary><c>IEndsWith</c> matches suffixes regardless of case.</summary>
    [Theory]
    [InlineData("user@example.COM", ".com", 1)]
    [InlineData("user@Example.Com", ".COM", 1)]
    [InlineData("user@example.org", ".com", 0)]
    public void IEndsWithMatchesCaseInsensitiveSuffix(string fieldValue, string suffix, int expectedCount)
    {
        var data = new List<SampleEntity> { new() { Name = fieldValue } }.AsQueryable();
        var predicate = FilterExpression.ParseFor<SampleEntity>($"Name$=*{suffix}")!;
        data.Where(predicate).Count().ShouldBe(expectedCount);
    }

    // ---------------------------------------------------------------------
    // NotIn (non-membership) operator
    // ---------------------------------------------------------------------

    /// <summary><c>NotIn</c> excludes matching enum values.</summary>
    [Fact(DisplayName = "When not In Excludes Matching Enum Values, then test passes")]
    public void NotInExcludesMatchingEnumValues()
    {
        var data = new List<SampleEntity>
        {
            new() { Status = SampleStatus.Active },
            new() { Status = SampleStatus.Trial },
            new() { Status = SampleStatus.Archived }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Status![]=Active,Trial")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Status.ShouldBe(SampleStatus.Archived);
    }

    /// <summary><c>NotIn</c> excludes matching Guid values.</summary>
    [Fact(DisplayName = "When not In Excludes Matching Guid Values, then test passes")]
    public void NotInExcludesMatchingGuidValues()
    {
        var included = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        var data = new List<SampleEntity>
        {
            new() { Id = included },
            new() { Id = excluded }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>($"Id![]={excluded}")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(included);
    }

    /// <summary><c>NotIn</c> excludes matching numeric values.</summary>
    [Fact(DisplayName = "When not In Excludes Matching Numeric Values, then test passes")]
    public void NotInExcludesMatchingNumericValues()
    {
        var data = new List<SampleEntity>
        {
            new() { Age = 10 },
            new() { Age = 20 },
            new() { Age = 30 }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Age![]=10,30")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Age.ShouldBe(20);
    }

    /// <summary><c>NotIn</c> combined with <c>Eq</c> via AND.</summary>
    [Fact(DisplayName = "When not In And Eq Combine, then test passes")]
    public void NotInAndEqCombine()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "Alice", Age = 20 },
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 20 }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Name==\"Alice\";Age![]=30")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Age.ShouldBe(20);
    }
}
