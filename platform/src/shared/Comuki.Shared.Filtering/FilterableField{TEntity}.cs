// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Linq.Expressions;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Descriptor for one filterable property of an entity. Carries the property's
///     name, CLR type, the set of <see cref="FilterOperator" /> the parser accepts for
///     it (inferred from the CLR type unless overridden), and the accessor expression
///     <c>x =&gt; x.Property</c> used to build the where-clause.
/// </summary>
/// <typeparam name="TEntity">Entity type the property belongs to.</typeparam>
/// <remarks>Constructs a field descriptor.</remarks>
/// <param name="name">Property name (case-insensitive on lookup).</param>
/// <param name="valueType">CLR type of the property's value.</param>
/// <param name="operators">Operators the parser accepts for this field.</param>
/// <param name="accessor">Accessor <c>x => x.Property</c> boxed to <c>object?</c>.</param>
public sealed class FilterableField<TEntity>(
    string name,
    Type valueType,
    FilterOperator operators,
    Expression<Func<TEntity, object?>> accessor)
{
    /// <summary>Property name as written in source (PascalCase). Lookups are case-insensitive.</summary>
    public string Name { get; } = name;

    /// <summary>CLR type of the value (used for parsing DSL literals).</summary>
    public Type ValueType { get; } = valueType;

    /// <summary>Operators allowed for this field. <see cref="FilterOperator.None" /> = not filterable.</summary>
    public FilterOperator Operators { get; } = operators;

    /// <summary>Accessor expression <c>x => x.Property</c> boxed to <c>object?</c>.</summary>
    public Expression<Func<TEntity, object?>> Accessor { get; } = accessor;
}
