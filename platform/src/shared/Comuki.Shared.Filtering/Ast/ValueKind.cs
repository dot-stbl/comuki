// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
namespace Comuki.Shared.Filtering.Ast;

/// <summary>
///     How the parser reads the value(s) following an operator token.
/// </summary>
public enum ValueKind
{
    /// <summary>
    ///     No value follows the operator (null-check predicates: <c>field?</c>,
    ///     <c>field!?</c>). The parser returns <see langword="null" />.
    /// </summary>
    None = 0,

    /// <summary>
    ///     A single scalar value follows: <c>field==value</c>. Parsed via
    ///     <c>ReadScalarValue</c> (bare or quoted, with <c>now(offset)</c> support).
    /// </summary>
    Scalar = 1,

    /// <summary>
    ///     A comma-separated list follows: <c>field[]=v1,v2,v3</c>. Parsed via
    ///     <c>ReadInValues</c> into an <see cref="System.Collections.IList" />.
    /// </summary>
    List = 2
}
