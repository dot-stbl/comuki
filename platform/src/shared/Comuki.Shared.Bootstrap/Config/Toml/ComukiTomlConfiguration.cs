using System.Globalization;
using Microsoft.Extensions.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace Comuki.Shared.Bootstrap.Config.Toml;

/// <summary>
/// TOML configuration source over <see cref="ComukiConfigFile"/>
/// (issue #54). The provider is always registered and optional: with no
/// config.toml on disk it yields an empty configuration instead of
/// failing — build-time OpenAPI generation relies on that. A malformed
/// file still throws: broken configuration must fail loudly at boot.
/// </summary>
public sealed class ComukiTomlConfigurationSource() : IConfigurationSource
{
    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new Provider();
    }
}

/// <summary>Loads the discovered config.toml into flattened configuration keys.</summary>
file sealed class Provider() : ConfigurationProvider
{
    public override void Load()
    {
        if (ComukiConfigFile.Find() is not { } path)
        {
            return;
        }

        // boundary: Tomlyn deserializer output — non-null by the Deserialize(typeof(TomlTable)) contract
        var model = (TomlTable)TomlSerializer.Deserialize(File.ReadAllText(path), typeof(TomlTable), TomlSerializerOptions.Default)!;
        TomlTableFlattener.Flatten(model, string.Empty, Data);
    }
}

/// <summary>Flattens a Tomlyn model into colon-separated configuration keys.</summary>
file static class TomlTableFlattener
{
    public static void Flatten(TomlTable table, string prefix, IDictionary<string, string?> data)
    {
        foreach (var (key, value) in table)
        {
            var configurationKey = prefix.Length == 0 ? key.ToLowerInvariant() : $"{prefix}:{key.ToLowerInvariant()}";

            switch (value)
            {
                case TomlTable nested:
                    Flatten(nested, configurationKey, data);
                    break;
                case TomlTableArray tableArray:
                    FlattenTableArray(tableArray, configurationKey, data);
                    break;
                case TomlArray array:
                    FlattenArray(array, configurationKey, data);
                    break;
                default:
                    data[configurationKey] = AsText(value);
                    break;
            }
        }
    }

    public static void FlattenTableArray(TomlTableArray tableArray, string configurationKey, IDictionary<string, string?> data)
    {
        for (var index = 0; index < tableArray.Count; index++)
        {
            Flatten(tableArray[index], $"{configurationKey}:{index}", data);
        }
    }

    public static void FlattenArray(TomlArray array, string configurationKey, IDictionary<string, string?> data)
    {
        for (var index = 0; index < array.Count; index++)
        {
            var itemKey = $"{configurationKey}:{index}";
            switch (array[index])
            {
                case TomlTable nested:
                    Flatten(nested, itemKey, data);
                    break;
                case TomlArray nestedArray:
                    FlattenArray(nestedArray, itemKey, data);
                    break;
                default:
                    data[itemKey] = AsText(array[index]);
                    break;
            }
        }
    }

    public static string? AsText(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
