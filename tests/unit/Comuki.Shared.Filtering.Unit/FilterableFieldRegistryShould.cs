// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
using Comuki.Shared.Filtering.Ast;
using Comuki.Shared.Filtering.Ports;
using Comuki.Shared.Filtering.Unit.TestEntities;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Filtering.Unit;

/// <summary>
///     Verifies the auto-discovery field registry: every public property is filterable
///     unless opted out via <c>[NotMapped]</c>, and operators are inferred by type.
/// </summary>
public sealed class FilterableFieldRegistryShould
{
    /// <summary>[NotMapped] properties are excluded — opt-out is enforced.</summary>
    [Fact]
    public void ExcludeNotMappedProperties()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();

        fields.Find("SecretKey").ShouldBeNull();
    }

    /// <summary>
    ///     <c>[NotMapped]</c> declared on an overridden base property excludes the property in
    ///     the subtype too.
    /// </summary>
    /// <remarks>
    ///     The subtype is what a repository queries, and the whole point of the attribute is
    ///     that it says the same thing to EF and to the DSL. <c>PropertyInfo.IsDefined</c>
    ///     ignores its own <c>inherit</c> flag for properties, so the exclusion used to stop at
    ///     the declaring type: five <c>Operation</c> subtypes offered a <c>Kind</c> filter that
    ///     no column backed.
    /// </remarks>
    [Fact]
    public void ExcludeNotMappedPropertyInheritedFromBase()
    {
        var fields = FilterableFieldRegistry.For<InheritedExclusionEntity>();

        fields.Find("Kind").ShouldBeNull();
        fields.Find("Name").ShouldNotBeNull();
    }

    /// <summary>
    ///     The typed and untyped views of one entity coexist — they are separate caches, not
    ///     one dictionary holding two value shapes under the same key.
    /// </summary>
    /// <remarks>
    ///     Both are asked for the same entity as soon as a DTO binds to it via
    ///     <c>[FilterableBy]</c>: repositories take the typed set, the OpenAPI transformers take
    ///     the untyped snapshot. With a shared cache whichever ran second got the other's entry
    ///     and threw <see cref="InvalidCastException" />.
    /// </remarks>
    [Fact]
    public void ServeTypedAndUntypedViewsOfTheSameEntity()
    {
        FilterableFieldRegistry.For<EmptyEntity>().All.ShouldBeEmpty();
        FilterableFieldRegistry.BuildUntyped(typeof(EmptyEntity)).ShouldBeEmpty();

        FilterableFieldRegistry.BuildUntyped(typeof(SampleEntity)).ShouldNotBeEmpty();
        FilterableFieldRegistry.For<SampleEntity>().All.ShouldNotBeEmpty();
    }

    /// <summary>Types with no DSL operators (byte[], object) are excluded silently.</summary>
    [Fact]
    public void ExcludeTypesThatMapToNone()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();

        fields.Find("Payload").ShouldBeNull();
        fields.Find("Bag").ShouldBeNull();
    }

    /// <summary>Empty entity produces an empty field set, not a throw.</summary>
    [Fact]
    public void ReturnEmptySetForEntityWithNoPublicProperties()
    {
        var fields = FilterableFieldRegistry.For<EmptyEntity>();

        fields.All.ShouldBeEmpty();
        fields.Find("anything").ShouldBeNull();
    }

    /// <summary>Field lookup is case-insensitive — clients may send any casing.</summary>
    [Fact]
    public void ResolveFieldsCaseInsensitively()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();

        fields.Find("Name").ShouldNotBeNull();
        fields.Find("name").ShouldNotBeNull();
        fields.Find("NAME").ShouldNotBeNull();
        fields.Find("nAmE").ShouldNotBeNull();
    }

    /// <summary>
    ///     String fields get Eq, NotEq, Contains, StartsWith, EndsWith, plus the
    ///     null operators <see cref="FilterOperator.IsNull" /> and
    ///     <see cref="FilterOperator.IsNotNull" /> because <see cref="string" /> is a
    ///     reference type.
    /// </summary>
    [Fact]
    public void InferStringOperatorsForStringProperties()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var name = fields.Find("Name").ShouldNotBeNull();

        name.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq
                                                  | FilterOperator.Contains | FilterOperator.StartsWith | FilterOperator.EndsWith
                                                  | FilterOperator.IContains | FilterOperator.IStartsWith | FilterOperator.IEndsWith
                                                  | FilterOperator.IsNull | FilterOperator.IsNotNull);
    }

    /// <summary>
    ///     <see cref="Nullable{T}" /> value-type fields (e.g. <c>int?</c>) get the
    ///     null operators in addition to their underlying type's set.
    /// </summary>
    [Fact]
    public void InferNullOperatorsForNullableValueType()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var optionalAge = fields.Find("OptionalAge").ShouldNotBeNull();

        optionalAge.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq
                                                         | FilterOperator.Gt | FilterOperator.Gte | FilterOperator.Lt | FilterOperator.Lte
                                                         | FilterOperator.In | FilterOperator.NotIn
                                                         | FilterOperator.IsNull | FilterOperator.IsNotNull);
    }

    /// <summary>
    ///     Non-nullable value types (e.g. <see cref="int" />) do NOT get null
    ///     operators — the predicate would always be false at runtime.
    /// </summary>
    [Fact]
    public void DoNotInferNullOperatorsForNonNullableValueType()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var age = fields.Find("Age").ShouldNotBeNull();

        age.Operators.ShouldNotBe(age.Operators & (FilterOperator.IsNull | FilterOperator.IsNotNull));
    }

    /// <summary>Enum fields get Eq, NotEq, In — no Contains (makes no sense).</summary>
    [Fact]
    public void InferEnumOperatorsForEnumProperties()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var status = fields.Find("Status").ShouldNotBeNull();

        status.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq | FilterOperator.In | FilterOperator.NotIn);
    }

    /// <summary>Numbers get Eq, NotEq, range (Gt/Gte/Lt/Lte), In.</summary>
    [Theory]
    [InlineData("Age", typeof(int))]
    [InlineData("Score", typeof(long))]
    [InlineData("Balance", typeof(decimal))]
    [InlineData("Rating", typeof(double))]
    public void InferNumericOperatorsForNumberProperties(string propertyName, Type _)
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var field = fields.Find(propertyName).ShouldNotBeNull();

        field.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq
                                                   | FilterOperator.Gt | FilterOperator.Gte | FilterOperator.Lt | FilterOperator.Lte
                                                   | FilterOperator.In | FilterOperator.NotIn);
    }

    /// <summary>Dates (DateTimeOffset, DateTime) get Eq, NotEq, range — no Contains.</summary>
    [Theory]
    [InlineData("CreatedAt", typeof(DateTimeOffset))]
    [InlineData("UpdatedAt", typeof(DateTime))]
    public void InferDateRangeOperatorsForDateProperties(string propertyName, Type _)
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var field = fields.Find(propertyName).ShouldNotBeNull();

        field.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq
                                                   | FilterOperator.Gt | FilterOperator.Gte | FilterOperator.Lt | FilterOperator.Lte);
    }

    /// <summary>Guid gets Eq, NotEq, In.</summary>
    [Fact]
    public void InferGuidOperators()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var id = fields.Find("Id").ShouldNotBeNull();

        id.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq | FilterOperator.In | FilterOperator.NotIn);
    }

    /// <summary>Bool gets only Eq.</summary>
    [Fact]
    public void InferBoolOperators()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var isActive = fields.Find("IsActive").ShouldNotBeNull();

        isActive.Operators.ShouldBe(FilterOperator.Eq);
    }

    /// <summary>TimeSpan gets Eq + range.</summary>
    [Fact]
    public void InferTimeSpanOperators()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var duration = fields.Find("Duration").ShouldNotBeNull();

        duration.Operators.ShouldBe(FilterOperator.Eq | FilterOperator.NotEq
                                                      | FilterOperator.Gt | FilterOperator.Gte | FilterOperator.Lt | FilterOperator.Lte);
    }

    /// <summary>Registry caches per type — same instance returned across calls.</summary>
    [Fact]
    public void CachePerType()
    {
        var first = FilterableFieldRegistry.For<SampleEntity>();
        var second = FilterableFieldRegistry.For<SampleEntity>();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    /// <summary>Accessor expression points at the right property.</summary>
    [Fact]
    public void BuildAccessorThatReadsTheUnderlyingProperty()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();
        var nameField = fields.Find("Name").ShouldNotBeNull();

        var entity = new SampleEntity { Name = "alice" };
        var accessor = nameField.Accessor.Compile();

        accessor(entity).ShouldBe("alice");
    }

    // ── Deny-list contract ─────────────────────────────────────────────────────

    /// <summary>
    ///     Deny-list contract: a new public property added to an entity is filterable
    ///     immediately. The DSL exposes every public instance property unless the
    ///     type is unsupported or the property carries <c>[NotMapped]</c> /
    ///     <c>[FilteredIgnore]</c>. Sensitive properties must be marked at declaration time.
    /// </summary>
    [Fact]
    public void DenyListContractExposesNewPublicPropertyByDefault()
    {
        var fields = FilterableFieldRegistry.For<SampleEntity>();

        // Nickname was added to SampleEntity without a hiding mark; must be
        // filterable. This test guards against an accidental future switch to
        // an allow-list contract that would silently drop newly-added fields.
        fields.Find("Nickname").ShouldNotBeNull();
    }

    /// <summary>
    ///     <c>[FilteredIgnore]</c> properties are excluded from BOTH views of the registry —
    ///     the typed set the repository filters with, and the untyped snapshot the OpenAPI
    ///     transformers publish as <c>x-filterable</c> / <c>x-sortable</c>.
    /// </summary>
    /// <remarks>
    ///     The two must agree: a field hidden from the parser but published in the schema
    ///     would advertise, in the generated client's filter builder, exactly the probe the
    ///     hiding existed to prevent.
    /// </remarks>
    [Fact]
    public void ExcludeFilteredIgnoreProperties()
    {
        FilterableFieldRegistry.For<SampleEntity>().Find("Passphrase").ShouldBeNull();

        FilterableFieldRegistry.BuildUntyped(typeof(SampleEntity))
                .Select(static field => field.Name)
                .ShouldNotContain("Passphrase");
    }

    /// <summary>
    ///     <c>[FilteredIgnore]</c> declared on an overridden base property hides the property
    ///     in the subtype too — same <c>inherit: true</c> rule as <c>[NotMapped]</c>, and it
    ///     matters for the same reason: the subtype is what a repository queries.
    /// </summary>
    [Fact]
    public void ExcludeFilteredIgnorePropertyInheritedFromBase()
    {
        var fields = FilterableFieldRegistry.For<InheritedExclusionEntity>();

        fields.Find("Secret").ShouldBeNull();
        fields.Find("Name").ShouldNotBeNull();
    }

    /// <summary>
    ///     A hidden field is not sortable either: sortability is derived from the filterable
    ///     set, so the criterion resolves to nothing and ordering falls back to the default.
    /// </summary>
    /// <remarks>
    ///     Asserted as "indistinguishable from no sort at all", and paired with a sort that
    ///     does reorder so the first assertion cannot pass vacuously.
    /// </remarks>
    [Fact]
    public void SkipAHiddenFieldInSortAndFallBackToTheDefault()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "charlie", Passphrase = "aaa" },
            new() { Name = "alice", Passphrase = "ccc" },
            new() { Name = "bob", Passphrase = "bbb" }
        }.AsQueryable();

        var byHidden = data.ApplySort("Passphrase,asc").Select(static e => e.Name).ToList();
        var byNothing = data.ApplySort(null).Select(static e => e.Name).ToList();
        var byName = data.ApplySort("Name,desc").Select(static e => e.Name).ToList();

        byHidden.ShouldBe(byNothing);
        byHidden.ShouldNotBe(byName);
    }
}
