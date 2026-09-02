// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using Comuki.Shared.Filtering.Ast;

namespace Comuki.Shared.Filtering.Parser;

/// <summary>
///     Abstract syntax tree node for a parsed filter expression. Produced by
///     <see cref="FilterParser" /> from a DSL string. Neutral — no TEntity coupling, no
///     EF-specific logic. Each consumer (EF translator, CH translator) walks the tree and
///     produces its own target type.
/// </summary>
public abstract class FilterNode;

/// <summary>
///     How the <see cref="ComparisonNode.Value" /> should be interpreted by the
///     translator. The neutral parser can't tell apart a quoted string <c>"a (b)"</c>
///     from a function call <c>now(-7d)</c> by looking at the string alone — the
///     token kind from the lexer is the only signal.
/// </summary>
public enum FilterValueKind
{
    /// <summary>Bare value (unquoted literal, function argument, or unquoted function call).</summary>
    Scalar = 0,

    /// <summary>Quoted string literal — the value is the text inside the quotes, verbatim.</summary>
    QuotedString = 1,

    /// <summary>
    ///     Function call (e.g. <c>now(-7d)</c>) — the value is the raw text
    ///     <c>"now(-7d)"</c>; the translator evaluates it (e.g. to a DateTimeOffset
    ///     for date/time fields). Detected by the parser from the
    ///     <c>Identifier + OpenParen</c> pattern in the lexer, not from the value
    ///     text alone.
    /// </summary>
    FunctionCall = 2
}

/// <summary>
///     Single comparison: <c>field op value</c>. Holds the field name (string), the
///     operator, and the already-parsed value. The consumer resolves the field name against
///     its own schema (EF: FilterableFieldSet; CH: TableSchema).
/// </summary>
/// <param name="Field"></param>
/// <param name="Operator"></param>
/// <param name="Value"></param>
public sealed class ComparisonNode(
    string Field,
    FilterOperator Operator,
    object? Value) : FilterNode
{
    /// <summary>The field name from the DSL.</summary>
    public string Field { get; } = Field;

    /// <summary>The comparison operator.</summary>
    public FilterOperator Operator { get; } = Operator;

    /// <summary>The raw parsed value (string, List&lt;string&gt;, or null).</summary>
    public object? Value { get; } = Value;

    /// <summary>
    ///     How the consumer should interpret <see cref="Value" />. Defaults to
    ///     <see cref="FilterValueKind.Scalar" /> for back-compat with consumers that
    ///     don't set this — the EF translator's old heuristic still applies.
    /// </summary>
    public FilterValueKind ValueKind { get; init; } = FilterValueKind.Scalar;
}

/// <summary>
///     Logical AND over child nodes. <c>a ; b ; c</c>.
/// </summary>
/// <param name="children"></param>
public sealed class AndNode(params FilterNode[] children) : FilterNode
{
    /// <summary>The child nodes.</summary>
    public FilterNode[] Children { get; } = children;
}

/// <summary>
///     Logical OR over child nodes. <c>a | b | c</c>.
/// </summary>
/// <param name="children"></param>
public sealed class OrNode(params FilterNode[] children) : FilterNode
{
    /// <summary>The child nodes.</summary>
    public FilterNode[] Children { get; } = children;
}
