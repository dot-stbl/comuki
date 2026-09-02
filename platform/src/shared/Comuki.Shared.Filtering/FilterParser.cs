// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Globalization;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Neutral recursive-descent parser: DSL string → <see cref="FilterNode" /> tree.
///     TEntity-free — does not resolve fields or convert types. Each comparison node holds
///     the raw field name + operator + raw string value(s). The consumer (EF translator, CH
///     translator) resolves the field against its own schema and converts the value.
/// </summary>
/// <remarks>
///     Grammar (lowest → highest precedence):
///     <code>
/// orExpr  := andExpr ('|' andExpr)*
/// andExpr := term (';' term)*
/// term    := '(' orExpr ')' | comparison
/// comparison := field operator value
///     </code>
///     AND binds tighter than OR (same as SQL); use parentheses to override.
/// </remarks>
public static class FilterParser
{
    /// <summary>
    ///     Parses a DSL string into a neutral <see cref="FilterNode" /> tree. Returns null
    ///     when <paramref name="source" /> is null/empty/whitespace.
    /// </summary>
    /// <param name="source">The DSL string (e.g. <c>"name~Apple;status==Active"</c>).</param>
    /// <param name="maxParenDepth">Maximum parenthesis nesting (default 32).</param>
    /// <exception cref="FilterParseException"></exception>
    public static FilterNode? Parse(string? source, int maxParenDepth = 32)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var tokens = FilterLexer.Tokenize(source);
        var state = new ParserState(tokens, maxParenDepth);
        var node = state.ParseOr();

        return state.Current.Kind != FilterTokenKind.EndOfInput
                ? throw new FilterParseException($"Unexpected token '{state.Current.Text}'", state.Current.Position)
                : node;
    }
}

/// <summary>
///     Mutable parser state — cursor, tokens, parenthesis depth. Not thread-safe; one per parse call.
/// </summary>
/// <param name="tokens"></param>
/// <param name="maxParenDepth"></param>
file sealed class ParserState(IReadOnlyList<FilterToken> tokens, int maxParenDepth)
{
    private readonly int maxDepth = maxParenDepth;
    private int cursor;
    private FilterValueKind lastValueKind = FilterValueKind.Scalar;
    private int parenDepth = maxParenDepth;

    public FilterToken Current => tokens[cursor];

    private FilterToken Peek(int offset = 1)
    {
        return cursor + offset < tokens.Count
                ? tokens[cursor + offset]
                : tokens[tokens.Count - 1];
    }

    public FilterToken Consume()
    {
        var token = tokens[cursor];

        if (cursor < tokens.Count - 1)
        {
            cursor++;
        }

        return token;
    }

    public FilterNode ParseOr()
    {
        var children = new List<FilterNode> { ParseAnd() };

        while (Current.Kind == FilterTokenKind.Or)
        {
            Consume();
            children.Add(ParseAnd());
        }

        return children.Count == 1 ? children[0] : new OrNode([.. children]);
    }

    private FilterNode ParseAnd()
    {
        var children = new List<FilterNode> { ParseTerm() };

        while (Current.Kind == FilterTokenKind.And)
        {
            Consume();
            children.Add(ParseTerm());
        }

        return children.Count == 1 ? children[0] : new AndNode([.. children]);
    }

    private FilterNode ParseTerm()
    {
        if (Current.Kind == FilterTokenKind.OpenParen)
        {
            if (parenDepth <= 0)
            {
                throw new FilterParseException(
                    string.Create(CultureInfo.InvariantCulture, $"Filter exceeds maximum parenthesis depth of {maxDepth}"),
                    Current.Position);
            }

            parenDepth--;
            Consume();
            var node = ParseOr();

            if (Current.Kind != FilterTokenKind.CloseParen)
            {
                throw new FilterParseException("Expected ')'", Current.Position);
            }

            parenDepth++;
            Consume();
            return node;
        }

        return ParseComparison();
    }

    private ComparisonNode ParseComparison()
    {
        if (Current.Kind != FilterTokenKind.Identifier)
        {
            throw new FilterParseException($"Expected field name, got '{Current.Text}'", Current.Position);
        }

        var fieldToken = Consume();

        if (Current.Kind != FilterTokenKind.Operator)
        {
            throw new FilterParseException($"Expected operator after field '{fieldToken.Text}'", Current.Position);
        }

        var operatorToken = Consume();
        var descriptor = FilterOperatorRegistry.Get(operatorToken.OperatorPayload);

        // Read raw value(s) — NO type conversion here. The translator resolves the field
        // and converts the value against the field's CLR type.
        object? value = descriptor.ValueKind switch
        {
            ValueKind.None => null,
            ValueKind.Scalar => ReadRawScalar(),
            ValueKind.List => ReadRawList(),
            _ => throw new NotSupportedException($"Unknown {nameof(ValueKind)} {descriptor.ValueKind}")
        };

        return new ComparisonNode(fieldToken.Text, operatorToken.OperatorPayload, value)
        {
            ValueKind = lastValueKind
        };
    }

    private string? ReadRawScalar()
    {
        // Function call: identifier followed by '(' — e.g. now(-7d).
        if (Current.Kind == FilterTokenKind.Identifier
            && Peek().Kind == FilterTokenKind.OpenParen)
        {
            lastValueKind = FilterValueKind.FunctionCall;
            return ReadFunctionCall();
        }

        lastValueKind = Current.Kind == FilterTokenKind.StringValue
                ? FilterValueKind.QuotedString
                : FilterValueKind.Scalar;

        return Current.Kind is not (FilterTokenKind.Value or FilterTokenKind.StringValue or FilterTokenKind.Identifier)
                ? throw new FilterParseException($"Expected value, got '{Current.Text}'", Current.Position)
                : (string?)Consume().Text;
    }

    /// <summary>
    ///     Reads a function call like <c>now(-7d)</c> and returns the raw string
    ///     <c>"now(-7d)"</c>. The translator evaluates it (e.g. to DateTimeOffset).
    /// </summary>
    /// <exception cref="FilterParseException"></exception>
    private string ReadFunctionCall()
    {
        var functionToken = Consume(); // 'now'
        Consume(); // '('

        if (Current.Kind is not (FilterTokenKind.Value or FilterTokenKind.Identifier))
        {
            throw new FilterParseException(
                $"Expected duration argument for '{functionToken.Text}()', got '{Current.Text}'",
                Current.Position);
        }

        var argumentToken = Consume();

        if (Current.Kind != FilterTokenKind.CloseParen)
        {
            throw new FilterParseException(
                $"Expected ')' to close '{functionToken.Text}(...)', got '{Current.Text}'",
                Current.Position);
        }

        Consume(); // ')'

        return $"{functionToken.Text}({argumentToken.Text})";
    }

    private List<string> ReadRawList()
    {
        if (Current.Kind is not (FilterTokenKind.Value or FilterTokenKind.StringValue or FilterTokenKind.Identifier))
        {
            throw new FilterParseException($"Expected value list, got '{Current.Text}'", Current.Position);
        }

        lastValueKind = Current.Kind == FilterTokenKind.StringValue
                ? FilterValueKind.QuotedString
                : FilterValueKind.Scalar;

        var list = new List<string> { Consume().Text };

        while (Current.Kind == FilterTokenKind.Comma)
        {
            Consume();

            if (Current.Kind is not (FilterTokenKind.Value or FilterTokenKind.StringValue or FilterTokenKind.Identifier))
            {
                throw new FilterParseException("Expected value after ','", Current.Position);
            }

            list.Add(Consume().Text);
        }

        return list;
    }
}
