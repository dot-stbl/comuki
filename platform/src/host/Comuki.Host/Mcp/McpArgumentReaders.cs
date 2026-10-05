using System.Globalization;
using System.Text.Json;

namespace Comuki.Host.Mcp;

/// <summary>
/// Argument readers for the MCP JSON-RPC <c>tools/call</c> dispatcher.
/// Each reader is a pure function over the parsed JSON arguments
/// object — no state, no side effects. ISO 8601 timestamps are
/// parsed with <see cref="DateTimeStyles.AssumeUniversal"/> +
/// <see cref="DateTimeStyles.AdjustToUniversal"/> so an unzoned
/// (<c>Z</c>-suffix-free) input is read as UTC, not local; the
/// observability wire contract is RFC3339 per
/// <c>specs/observability/spec.md</c>, and the typed clients convert to
/// unix-seconds (Prometheus) or RFC3339 (LogsQL <c>time</c> pipe) as
/// required.
/// </summary>
internal static class McpArgumentReaders
{
    /// <summary>Reads a required string property; returns <see cref="string.Empty"/> when absent or non-string.</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Name of the property the tool's JSON schema marks as required.</param>
    public static string ReadString(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>Reads an optional string property; returns <c>null</c> when absent or whitespace.</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name to read; case-insensitive match against the wire JSON keys.</param>
    public static string? ReadOptionalString(JsonElement arguments, string propertyName)
    {
        var value = ReadString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Reads an optional Guid property; returns <c>null</c> when absent or unparseable.</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the Guid string (e.g. <c>projectId</c>).</param>
    public static Guid? ReadOptionalGuid(JsonElement arguments, string propertyName)
    {
        var raw = ReadOptionalString(arguments, propertyName);
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Reads an optional long property; accepts numbers or numeric strings.
    /// Integer <c>int?</c> callers go through the same path
    /// (<see cref="ReadOptionalInt"/>) — checked-cast on the long result
    /// so the wire shape stays one shape (numeric + numeric-string).
    /// </summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the numeric value (e.g. <c>limit</c>).</param>
    public static long? ReadOptionalLong(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var value))
                {
                    return value;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && long.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    /// <summary>Reads an optional int property; routed through <see cref="ReadOptionalLong"/> with a checked range check.</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the int value (e.g. <c>topK</c>).</param>
    public static int? ReadOptionalInt(JsonElement arguments, string propertyName)
    {
        var value = ReadOptionalLong(arguments, propertyName);
        return value is null ? null : checked((int)value.Value);
    }

    /// <summary>Reads an optional bool property; accepts booleans or boolean strings.</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the boolean value (e.g. <c>ephemeral</c>).</param>
    public static bool? ReadOptionalBool(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && bool.TryParse(property.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    /// <summary>Reads an optional float property; accepts numbers or numeric strings (invariant culture).</summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the float value (e.g. <c>minSimilarity</c>).</param>
    public static float? ReadOptionalFloat(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetSingle(out var value))
                {
                    return value;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && float.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an optional ISO 8601 timestamp property; returns <c>null</c>
    /// when absent. Throws <see cref="ArgumentException"/> on a
    /// non-empty value that fails the parse — the validation clamp the
    /// brief mandates (<c>specs/observability/spec.md</c> "accepts
    /// <c>{... from?: iso8601, to?: iso8601, ...}</c>"). The
    /// invariant culture + <see cref="DateTimeStyles.AssumeUniversal"/>
    /// + <see cref="DateTimeStyles.AdjustToUniversal"/> combination
    /// makes the parse a pure UTC read: an unzoned input is
    /// interpreted as UTC (the wire shape Victoria expects), not as
    /// the host's local zone (the deployment baseline runs in UTC
    /// anyway; the styles guard against a CI runner in a different
    /// zone silently flipping the parsed value).
    /// </summary>
    /// <param name="arguments">Parsed JSON-RPC <c>tools/call</c> arguments object.</param>
    /// <param name="propertyName">Property name carrying the ISO 8601 string (e.g. <c>from</c>, <c>to</c>).</param>
    public static DateTimeOffset? ReadOptionalIso8601(JsonElement arguments, string propertyName)
    {
        var raw = ReadOptionalString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(raw)
            ? null
            : DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed)
                ? parsed
                : throw new ArgumentException($"arguments.{propertyName} must be an ISO 8601 timestamp (got: {raw}).", propertyName);
    }
}
