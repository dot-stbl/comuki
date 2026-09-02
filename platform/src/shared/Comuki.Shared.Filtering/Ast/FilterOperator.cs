// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
namespace Comuki.Shared.Filtering.Ast;

/// <summary>
///     Comparison operators supported by the filter DSL. A field's allowed set is
///     inferred from its CLR type (see <see cref="FilterOperatorInference" />); the parser
///     rejects a query that uses an operator the field does not allow.
/// </summary>
/// <remarks>
///     <para>
///         Flags enum so the type-inference table can declare
///         <c>FilterOperator.Eq | FilterOperator.In</c> (enum field) or
///         <c>FilterOperator.Contains | FilterOperator.StartsWith</c> (text field)
///         in one expression.
///     </para>
///     <para>
///         The DSL literal for each operator is documented on the member; the
///         lexer maps the literal to this enum.
///     </para>
/// </remarks>
[Flags]
public enum FilterOperator
{
    /// <summary>
    ///     No operators. Returned by type-inference for CLR types the DSL does not
    ///     support (e.g. <c>byte[]</c>, <c>object</c>). Fields of such types are
    ///     excluded from the field registry.
    /// </summary>
    None = 0,

    /// <summary>Equality. DSL: <c>field==value</c>.</summary>
    Eq = 1,

    /// <summary>Inequality. DSL: <c>field!=value</c>.</summary>
    NotEq = 1 << 1,

    /// <summary>Substring match. DSL: <c>field~value</c>. Strings only.</summary>
    Contains = 1 << 2,

    /// <summary>Prefix match. DSL: <c>field^=value</c>. Strings only.</summary>
    StartsWith = 1 << 3,

    /// <summary>Suffix match. DSL: <c>field$=value</c>. Strings only.</summary>
    EndsWith = 1 << 4,

    /// <summary>Strictly greater than. DSL: <c>field&gt;value</c>. Numbers and dates.</summary>
    Gt = 1 << 5,

    /// <summary>Greater than or equal. DSL: <c>field&gt;=value</c>. Numbers and dates.</summary>
    Gte = 1 << 6,

    /// <summary>Strictly less than. DSL: <c>field&lt;value</c>. Numbers and dates.</summary>
    Lt = 1 << 7,

    /// <summary>Less than or equal. DSL: <c>field&lt;=value</c>. Numbers and dates.</summary>
    Lte = 1 << 8,

    /// <summary>
    ///     Membership in a comma-separated list. DSL: <c>field[]=v1,v2,v3</c>.
    ///     Enums, numbers, and Guids.
    /// </summary>
    In = 1 << 9,

    /// <summary>
    ///     Null check. DSL: <c>field?</c>. Reference types and <see cref="Nullable{T}" />.
    ///     Translates to <c>entity.field == null</c>.
    /// </summary>
    IsNull = 1 << 10,

    /// <summary>
    ///     Not-null check. DSL: <c>field!?</c>. Reference types and <see cref="Nullable{T}" />.
    ///     Translates to <c>entity.field != null</c>.
    /// </summary>
    IsNotNull = 1 << 11,

    /// <summary>
    ///     Case-insensitive substring match. DSL: <c>field~*value</c>. Strings only.
    ///     Translates to <c>field.ToLower().Contains(value.ToLower())</c>.
    /// </summary>
    IContains = 1 << 12,

    /// <summary>
    ///     Case-insensitive prefix match. DSL: <c>field^=*value</c>. Strings only.
    /// </summary>
    IStartsWith = 1 << 13,

    /// <summary>
    ///     Case-insensitive suffix match. DSL: <c>field$=*value</c>. Strings only.
    /// </summary>
    IEndsWith = 1 << 14,

    /// <summary>
    ///     Non-membership in a comma-separated list. DSL: <c>field![]=v1,v2,v3</c>.
    ///     Enums, numbers, and Guids. Negation of <see cref="In" />.
    /// </summary>
    NotIn = 1 << 15
}
