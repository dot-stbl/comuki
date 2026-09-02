// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Reflects an entity type into a map of filterable fields. Every public
///     instance property is filterable unless it is annotated <c>[NotMapped]</c> or
///     <see cref="FilteredIgnoreAttribute" />, or its CLR type maps to
///     <see cref="FilterOperator.None" />. Operator sets are inferred by
///     <see cref="FilterOperatorInference" />.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <param name="fields"></param>
/// <remarks>Constructs from a pre-built field dictionary.</remarks>
public sealed class FilterableFieldSet<TEntity>(IReadOnlyDictionary<string, FilterableField<TEntity>> fields)
{
    private readonly Dictionary<string, FilterableField<TEntity>> fieldMap = new(fields, StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered fields (for OpenAPI schema emission + diagnostics).</summary>
    public IReadOnlyCollection<FilterableField<TEntity>> All => fieldMap.Values;

    /// <summary>
    ///     Looks up a field by name (case-insensitive). Returns <c>null</c> when the
    ///     field is unknown or excluded from the registry — the parser surfaces this as
    ///     a <see cref="FilterParseException" />.
    /// </summary>
    /// <param name="name"></param>
    public FilterableField<TEntity>? Find(string name)
    {
        return fieldMap.TryGetValue(name, out var field) ? field : null;
    }
}

/// <summary>
///     Builds <see cref="FilterableFieldSet{TEntity}" /> via reflection, and caches
///     the result per entity type. Reflection happens at most once per type.
/// </summary>
public static class FilterableFieldRegistry
{
    /// <summary>
    ///     Two caches, not one, and both keyed by the same entity type. A single
    ///     dictionary held two different value shapes under one key, so whichever of
    ///     <see cref="For{TEntity}" /> / <see cref="BuildUntyped" /> ran second for a
    ///     given entity got the other one's entry and threw
    ///     <see cref="InvalidCastException" />. It never fired only because the two
    ///     callers used to see disjoint type sets — repositories asked for entities,
    ///     the OpenAPI transformers asked for DTOs. Since <see cref="FilterableByAttribute" />
    ///     the transformers ask for the entity too, which is exactly the collision.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, object> typedCache = new();

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<UntypedFilterableField>> untypedCache = new();

    /// <summary>Returns the cached field set for <typeparamref name="TEntity" />, building it on first use.</summary>
    /// <typeparam name="TEntity"></typeparam>
    public static FilterableFieldSet<TEntity> For<TEntity>()
    {
        return (FilterableFieldSet<TEntity>)typedCache.GetOrAdd(typeof(TEntity), static _ => FilterableFieldSetBuilder.Build<TEntity>());
    }

    /// <summary>
    ///     Returns a typed-erased snapshot of the filterable fields for <paramref name="type" />.
    ///     Used by OpenAPI schema transformers and other reflection-only consumers
    ///     that cannot name a generic <c>TEntity</c> at compile time.
    /// </summary>
    /// <param name="type"></param>
    public static IReadOnlyList<UntypedFilterableField> BuildUntyped(Type type)
    {
        return untypedCache.GetOrAdd(type, static t => FilterableFieldSetBuilder.BuildUntypedCore(t));
    }
}

/// <summary>
///     Field descriptor without a generic entity parameter — for reflection-based
///     consumers (OpenAPI schema transformer, diagnostics). Carries only the static
///     metadata (<see cref="Name" />, <see cref="ValueType" />, <see cref="Operators" />)
///     without the accessor expression, which needs a generic type.
/// </summary>
/// <param name="Name"></param>
/// <param name="ValueType"></param>
/// <param name="Operators"></param>
public sealed record UntypedFilterableField(string Name, Type ValueType, FilterOperator Operators);

file static class FilterableFieldSetBuilder
{
    /// <summary>
    ///     Field exposure contract: every public instance property of an entity is filterable
    ///     unless the property is annotated <c>[NotMapped]</c> or
    ///     <see cref="FilteredIgnoreAttribute" />, is an indexer, or has a CLR type the DSL does
    ///     not support (e.g. <c>byte[]</c>, <c>object</c>). A deny-list, in one mode — there is
    ///     no per-entity strict switch.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <remarks>
    ///     <para>
    ///         Sensitive properties (credentials, hashes, session and permission stamps) MUST be
    ///         marked <see cref="FilteredIgnoreAttribute" /> at declaration time — adding a new
    ///         public sensitive property without it exposes the property to filter queries
    ///         immediately, and to sort and to the published <c>x-filterable</c> contract with
    ///         it. Treat this as "every public property is readable until proven otherwise";
    ///         what that costs and how to pay it down is written on the attribute itself.
    ///     </para>
    ///     <para>
    ///         <c>[NotMapped]</c> is honoured for a different reason and is not a substitute: a
    ///         property with no column cannot be translated to SQL at all. A property that is
    ///         genuinely persisted and merely must not be searchable — the account aggregate's
    ///         password hash is the first of them — is the case
    ///         <see cref="FilteredIgnoreAttribute" /> exists for.
    ///     </para>
    /// </remarks>
    public static FilterableFieldSet<TEntity> Build<TEntity>()
    {
        var type = typeof(TEntity);
        var parameter = Expression.Parameter(type, "x");
        var fields = new Dictionary<string, FilterableField<TEntity>>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (PropertyExclusions.IsExcluded(property))
            {
                continue;
            }

            var operators = FilterOperatorInference.Infer(property.PropertyType);

            if (operators == FilterOperator.None)
            {
                continue;
            }

            var accessor = Expression.Lambda<Func<TEntity, object?>>(
                Expression.Convert(Expression.Property(parameter, property), typeof(object)),
                parameter);

            fields[property.Name] = new FilterableField<TEntity>(
                property.Name,
                property.PropertyType,
                operators,
                accessor);
        }

        return new FilterableFieldSet<TEntity>(fields);
    }

    /// <summary>
    ///     Reflection-only variant — produces <see cref="UntypedFilterableField" />
    ///     snapshots without building accessor expressions. Used by the OpenAPI schema
    ///     transformer, which needs only the static metadata (name + operators) per field.
    /// </summary>
    /// <remarks>
    ///     See <see cref="Build{TEntity}" /> for the exposure contract, which applies here
    ///     unchanged. The two must agree exactly: this one describes the endpoint in OpenAPI
    ///     while that one decides what the endpoint actually accepts, so a field published here
    ///     and rejected there is a documented query that answers <c>400</c> — and a field hidden
    ///     there but published here would advertise the very probe the hiding was for.
    /// </remarks>
    /// <param name="type"></param>
    public static IReadOnlyList<UntypedFilterableField> BuildUntypedCore(Type type)
    {
        var fields = new List<UntypedFilterableField>();

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (PropertyExclusions.IsExcluded(property))
            {
                continue;
            }

            var operators = FilterOperatorInference.Infer(property.PropertyType);

            if (operators == FilterOperator.None)
            {
                continue;
            }

            fields.Add(new UntypedFilterableField(property.Name, property.PropertyType, operators));
        }

        return fields;
    }
}

file static class PropertyExclusions
{
    /// <summary>
    ///     Returns <c>true</c> when the property must not be filterable: an indexer (cannot be
    ///     expressed as <c>x =&gt; x.P</c>), an explicit <c>[NotMapped]</c>, or an explicit
    ///     <see cref="FilteredIgnoreAttribute" /> — either attribute counting when it is declared
    ///     on a base-class property this one overrides.
    /// </summary>
    /// <param name="property"></param>
    /// <remarks>
    ///     <see cref="Attribute.IsDefined(MemberInfo, Type, bool)" /> and not
    ///     <c>property.IsDefined(…, inherit: true)</c>: for properties the latter's
    ///     <c>inherit</c> flag does nothing — only the static <see cref="Attribute" /> helper
    ///     walks to the overridden declaration. The difference is not academic. An abstract
    ///     property marked <c>[NotMapped]</c> on the base (<c>Operation.Kind</c>) stayed
    ///     excluded for the base type and came back for all five overriding subtypes, which
    ///     are exactly the types the repository queries.
    /// </remarks>
    public static bool IsExcluded(PropertyInfo property)
    {
        return property.GetIndexParameters().Length > 0
               || Attribute.IsDefined(property, typeof(NotMappedAttribute), true)
               || Attribute.IsDefined(property, typeof(FilteredIgnoreAttribute), true);
    }
}
