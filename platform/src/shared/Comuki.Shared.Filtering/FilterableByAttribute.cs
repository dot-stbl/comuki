// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
namespace Comuki.Shared.Filtering;

/// <summary>
///     Binds a read DTO (the wire shape a list endpoint returns) to the entity the
///     server actually filters and sorts. The published <c>x-filterable</c> /
///     <c>x-sortable</c> contract is built from
///     <see cref="FilterableFieldRegistry" /> for <see cref="EntityType" />, not from
///     the DTO's own properties.
/// </summary>
/// <param name="entityType">
///     The entity the endpoint's repository applies <c>ApplyFilter</c> / <c>ApplySort</c>
///     to — the type passed to <see cref="FilterableFieldRegistry.For{TEntity}" />.
/// </param>
/// <remarks>
///     <para>
///         <b>Why the attribute and not a naming convention.</b> The DSL is parsed
///         against the entity: <c>FilterExpressionParser&lt;TEntity&gt;</c> resolves
///         field names in the entity's registry, and the expression it builds is
///         translated by EF against entity columns. A description derived from the DTO
///         therefore describes something that is never executed. The two shapes diverge
///         in both directions — a DTO may flatten or rename entity fields, and it may
///         drop fields the server still filters on. A name convention cannot express
///         that link either: DTO and entity live in different assemblies and the
///         mapping is not one-to-one. An explicit, compile-checked reference is the
///         only form that says which entity is meant and fails to build when that
///         entity is renamed or deleted.
///     </para>
///     <para>
///         <b>No pair, no extension.</b> A schema without this attribute publishes no
///         <c>x-filterable</c> / <c>x-sortable</c> at all. That is deliberate: silence
///         is the honest description of "this shape is not a filterable list".
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class FilterableByAttribute(Type entityType) : Attribute
{
    /// <summary>The entity whose filterable-field registry describes this DTO's endpoint.</summary>
    public Type EntityType { get; } = entityType;
}
