// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Text;

namespace Comuki.Shared.Filtering;

/// <summary>Kind of token the <see cref="FilterLexer" /> emits.</summary>
public enum FilterTokenKind
{
    /// <summary>End of input.</summary>
    EndOfInput = 0,

    /// <summary>Field name (identifier).</summary>
    Identifier = 1,

    /// <summary>String literal (<c>"..."</c>).</summary>
    StringValue = 2,

    /// <summary>Bare value (number, enum name, date fragment, Guid).</summary>
    Value = 3,

    /// <summary>One of the comparison operators (==, !=, ~, ^=, $=, &gt;, &gt;=, &lt;, &lt;=, []=).</summary>
    Operator = 4,

    /// <summary>Logical AND (<c>;</c>).</summary>
    And = 5,

    /// <summary>Logical OR (<c>|</c>).</summary>
    Or = 6,

    /// <summary>Open parenthesis.</summary>
    OpenParen = 7,

    /// <summary>Close parenthesis.</summary>
    CloseParen = 8,

    /// <summary>Comma inside <c>[]=</c> list values.</summary>
    Comma = 9
}

/// <summary>
///     Single token produced by the lexer. <see cref="OperatorPayload" /> carries the
///     resolved <see cref="FilterOperator" /> for <see cref="FilterTokenKind.Operator" />.
/// </summary>
/// <param name="Kind"></param>
/// <param name="Text"></param>
/// <param name="OperatorPayload"></param>
/// <param name="Position"></param>
public sealed record FilterToken(FilterTokenKind Kind, string Text, FilterOperator OperatorPayload, int Position);

/// <summary>
///     Splits the DSL source into tokens. Zero-allocation hot path uses a single
///     <see cref="StringBuilder" /> for the current token; operators are matched by
///     longest-prefix so <c>&gt;=</c> wins over <c>&gt;</c>.
/// </summary>
internal sealed class FilterLexer
{
    /// <summary>
    ///     Hard cap on the number of tokens a single filter query may produce.
    ///     Bounds CPU and allocation work for adversarial inputs that emit thousands
    ///     of <c>;</c>/<c>|</c> clauses (audit finding 1f). 256 covers any realistic
    ///     list-filter UI while keeping DoS surface small.
    /// </summary>
    public const int MaxTokens = 256;

    private readonly string sourceText;
    private readonly StringBuilder tokenBuilder = new();
    private int positionIndex;

    private FilterLexer(string source)
    {
        sourceText = source;
        positionIndex = 0;
    }

    /// <summary>
    ///     Tokenizes <paramref name="source" /> into a flat list. Throws
    ///     <see cref="FilterParseException" /> on unterminated strings, on
    ///     <see cref="MaxTokens" />+1 emitted tokens, or on any unexpected character.
    /// </summary>
    /// <param name="source"></param>
    /// <exception cref="FilterParseException"></exception>
    public static IReadOnlyList<FilterToken> Tokenize(string source)
    {
        var lexer = new FilterLexer(source);
        var tokens = new List<FilterToken>();

        while (lexer.Next() is { } token)
        {
            if (tokens.Count >= MaxTokens)
            {
                throw new FilterParseException(
                    $"Filter exceeds maximum of {MaxTokens} tokens",
                    lexer.positionIndex);
            }

            tokens.Add(token);
        }

        tokens.Add(new FilterToken(FilterTokenKind.EndOfInput, string.Empty, FilterOperator.None, lexer.positionIndex));
        return tokens;
    }

    private FilterToken? Next()
    {
        SkipWhitespace();

        if (positionIndex >= sourceText.Length)
        {
            return null;
        }

        var start = positionIndex;
        var ch = sourceText[positionIndex];

        return ch switch
        {
            '(' => Single(FilterTokenKind.OpenParen, start),
            ')' => Single(FilterTokenKind.CloseParen, start),
            ';' => Single(FilterTokenKind.And, start),
            '|' => Single(FilterTokenKind.Or, start),
            ',' => Single(FilterTokenKind.Comma, start),
            '"' => ReadQuotedString(start),
            _ => ReadOperatorOrValue(start)
        };
    }

    private FilterToken Single(FilterTokenKind kind, int position)
    {
        positionIndex++;
        return new FilterToken(kind, sourceText.AsSpan(position, 1).ToString(), FilterOperator.None, position);
    }

    private FilterToken ReadQuotedString(int start)
    {
        positionIndex++; // consume opening "
        tokenBuilder.Clear();

        while (positionIndex < sourceText.Length)
        {
            var ch = sourceText[positionIndex];

            // Backslash escapes the next character literally: `\"` is a quote inside
            // the value, `\\` a single backslash. Mirrors the client serializer's
            // escaping (kubb-plugin-filter): without this the first `\"` would close
            // the string early and `\\` would survive doubled. Not C-style — `\n` is a
            // literal `n`, not a newline; only `"` and `\` need escaping on the wire.
            if (ch == '\\')
            {
                positionIndex++;

                if (positionIndex >= sourceText.Length)
                {
                    throw new FilterParseException("Unterminated escape in string literal", start);
                }

                tokenBuilder.Append(sourceText[positionIndex]);
                positionIndex++;
                continue;
            }

            if (ch == '"')
            {
                positionIndex++;
                return new FilterToken(FilterTokenKind.StringValue, tokenBuilder.ToString(), FilterOperator.None, start);
            }

            tokenBuilder.Append(ch);
            positionIndex++;
        }

        throw new FilterParseException("Unterminated string literal", start);
    }

    private FilterToken ReadOperatorOrValue(int start)
    {
        var ch = sourceText[positionIndex];

        // Operator longest-prefix match. The registry sorts symbols by descending
        // length, so `~*` is tested before `~` and `![]=` before `!=`. Adding an
        // operator means adding a symbol to FilterOperatorRegistry — no edit here.
        foreach (var (symbol, op) in FilterOperatorRegistry.SymbolsByDescendingLength)
        {
            if (MatchesAt(symbol, start))
            {
                return OperatorToken(op, symbol.Length, start);
            }
        }

        // Otherwise — bare value. Read until we hit a delimiter.
        tokenBuilder.Clear();

        while (positionIndex < sourceText.Length && IsValueChar(sourceText[positionIndex]))
        {
            tokenBuilder.Append(sourceText[positionIndex]);
            positionIndex++;
        }

        if (tokenBuilder.Length == 0)
        {
            throw new FilterParseException($"Unexpected character '{ch}'", start);
        }

        var text = tokenBuilder.ToString();
        // A bare identifier directly preceding an operator is a field name.
        return IsValidIdentifier(text)
                ? new FilterToken(FilterTokenKind.Identifier, text, FilterOperator.None, start)
                : new FilterToken(FilterTokenKind.Value, text, FilterOperator.None, start);
    }

    /// <summary>
    ///     Checks whether <paramref name="symbol" /> occurs at the current
    ///     position. Does not advance the position — the caller does that on a match.
    /// </summary>
    /// <param name="symbol"></param>
    /// <param name="start"></param>
    private bool MatchesAt(string symbol, int start)
    {
        if (start + symbol.Length > sourceText.Length)
        {
            return false;
        }

        for (var offset = 0; offset < symbol.Length; offset++)
        {
            if (sourceText[start + offset] != symbol[offset])
            {
                return false;
            }
        }

        return true;
    }

    private FilterToken OperatorToken(FilterOperator op, int length, int start)
    {
        positionIndex += length;
        return new FilterToken(FilterTokenKind.Operator, op.ToString(), op, start);
    }

    private void SkipWhitespace()
    {
        while (positionIndex < sourceText.Length && char.IsWhiteSpace(sourceText[positionIndex]))
        {
            positionIndex++;
        }
    }

    private static bool IsValueChar(char ch)
    {
        return !char.IsWhiteSpace(ch)
               && ch is not ('(' or ')' or ';' or '|' or ',' or '"' or '=' or '!' or '~' or '^' or '$' or '<' or '>' or '[' or '?');
    }

    private static bool IsValidIdentifier(string text)
    {
        return text.Length > 0
               && (char.IsLetter(text[0]) || text[0] == '_')
               && text.All(static c => char.IsLetterOrDigit(c) || c == '_');
    }
}
