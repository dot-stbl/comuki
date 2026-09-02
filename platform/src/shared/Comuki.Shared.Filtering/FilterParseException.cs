// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Globalization;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Thrown by the filter DSL parser when the query string is malformed, references
///     an unknown field, uses an operator the field does not allow, or carries a value
///     that cannot be converted to the field's CLR type. Surfaces to the client as
///     <c>400 Bad Request</c> via <c>AddProblemDetails()</c>.
/// </summary>
public sealed class FilterParseException : FormatException
{
    /// <summary>Constructs with a descriptive message and zero-based source position.</summary>
    /// <param name="message"></param>
    /// <param name="position"></param>
    public FilterParseException(string message, int position = -1)
            : base(position < 0 ? message : string.Create(CultureInfo.InvariantCulture, $"{message} (at position {position})"))
    {
        Position = position;
    }

    /// <summary>Constructs with a descriptive message, position, and inner cause.</summary>
    /// <param name="message"></param>
    /// <param name="position"></param>
    /// <param name="inner"></param>
    public FilterParseException(string message, int position, Exception inner)
            : base(position < 0 ? message : string.Create(CultureInfo.InvariantCulture, $"{message} (at position {position})"), inner)
    {
        Position = position;
    }

    /// <summary>Zero-based index in the source string where the error was detected, or <c>-1</c>.</summary>
    public int Position { get; }
}
