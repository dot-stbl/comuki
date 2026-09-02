// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Linq.Expressions;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Declarative descriptor for a single <see cref="FilterOperator" />. Every
///     subsystem (lexer, inference, parser, expression builder) reads from the
///     registry instead of carrying its own switch — adding an operator is one entry
///     in <see cref="FilterOperatorRegistry" />, not a five-file edit.
/// </summary>
/// <param name="Operator">The enum value this descriptor describes.</param>
/// <param name="Symbol">Wire DSL literal (e.g. <c>"~*"</c>). Longest-prefix match in the lexer.</param>
/// <param name="ValueKind">How the parser reads the value following the symbol.</param>
/// <param name="SupportsType">Returns <c>true</c> when a CLR property type is allowed to use this operator.</param>
/// <param name="BuildExpression">
///     Builds the LINQ <see cref="Expression" /> for <c>field op value</c>.
///     <para>
///         <c>fieldAccessor</c> is the entity property expression (unwrapped from boxing).
///         <c>value</c> is the parsed runtime value (typed by the field's CLR type).
///         <c>valueType</c> is the field's CLR type (needed for <c>In</c>/<c>NotIn</c>).
///     </para>
///     For <see cref="FilterOperator.IsNull" />/<see cref="FilterOperator.IsNotNull" />
///     <c>value</c> is <see langword="null" />.
/// </param>
public sealed record FilterOperatorDescriptor(
    FilterOperator Operator,
    string Symbol,
    ValueKind ValueKind,
    Func<Type, bool> SupportsType,
    Func<Expression, object?, Type, Expression> BuildExpression);
