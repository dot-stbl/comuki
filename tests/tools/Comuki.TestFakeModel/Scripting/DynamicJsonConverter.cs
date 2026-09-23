using System.Collections;
using System.Text.Json;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Normalizes a loosely-typed "tool input" value — a <see cref="JsonElement"/>
/// already (the JSON fakeScript loader's shape, System.Text.Json's native
/// deserialize-to-<c>object</c> result), or the dynamic
/// <see cref="IDictionary"/>/<see cref="IEnumerable"/>/scalar graph
/// YamlDotNet produces for an <c>object</c>-typed property — into a
/// <see cref="JsonElement"/> the Anthropic wire models can embed directly.
/// </summary>
internal static class DynamicJsonConverter
{
    /// <summary>The empty JSON object <c>{}</c> — every <c>tool_use</c> block's <c>input</c> defaults to this, never <c>null</c>.</summary>
    public static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

    /// <summary>Converts <paramref name="value"/> to a <see cref="JsonElement"/>, defaulting to <see cref="EmptyObject"/> for <c>null</c>.</summary>
    public static JsonElement ToJsonElement(object? value)
    {
        return value switch
        {
            null => EmptyObject,
            JsonElement element => element,
            _ => JsonSerializer.SerializeToElement(Normalize(value)),
        };
    }

    private static object? Normalize(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement => value,
            string text => text,
            IDictionary dictionary => NormalizeDictionary(dictionary),
            IEnumerable enumerable => NormalizeSequence(enumerable),
            _ => value,
        };
    }

    private static Dictionary<string, object?> NormalizeDictionary(IDictionary dictionary)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            map[entry.Key.ToString() ?? string.Empty] = Normalize(entry.Value);
        }

        return map;
    }

    private static List<object?> NormalizeSequence(IEnumerable enumerable)
    {
        var list = new List<object?>();
        foreach (var item in enumerable)
        {
            list.Add(Normalize(item));
        }

        return list;
    }
}
