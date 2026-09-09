using System.Text.Json;

namespace Comuki.Host.Mcp;

/// <summary>
/// Argument readers for the MCP JSON-RPC <c>tools/call</c> dispatcher.
/// Extracted from <c>McpServer</c> so the dispatcher class holds only
/// orchestration. Each reader is a pure function over the parsed JSON
/// arguments object — no state, no side effects.
/// </summary>
internal static class McpArgumentReaders
{
    /// <summary>Reads a required string property; returns <see cref="string.Empty"/> when absent or non-string.</summary>
    /// <param name="arguments"></param>
    /// <param name="propertyName"></param>
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
    /// <param name="arguments"></param>
    /// <param name="propertyName"></param>
    public static string? ReadOptionalString(JsonElement arguments, string propertyName)
    {
        var value = ReadString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Reads an optional Guid property; returns <c>null</c> when absent or unparseable.</summary>
    /// <param name="arguments"></param>
    /// <param name="propertyName"></param>
    public static Guid? ReadOptionalGuid(JsonElement arguments, string propertyName)
    {
        var raw = ReadOptionalString(arguments, propertyName);
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    /// <summary>Reads an optional int property; accepts numbers or numeric strings.</summary>
    /// <param name="arguments"></param>
    /// <param name="propertyName"></param>
    public static int? ReadOptionalInt(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                {
                    return value;
                }

                if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    /// <summary>Reads an optional float property; accepts numbers or numeric strings (invariant culture).</summary>
    /// <param name="arguments"></param>
    /// <param name="propertyName"></param>
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
                    && float.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }
}
