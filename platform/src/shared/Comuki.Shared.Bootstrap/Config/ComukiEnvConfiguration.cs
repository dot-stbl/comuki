using System.Collections;
using Comuki.Shared.Bootstrap.Config.Toml;
using Microsoft.Extensions.Configuration;

namespace Comuki.Shared.Bootstrap.Config;

/// <summary>
/// Environment-variable configuration source with the Comuki face
/// (issue #54): every <c>COMUKI_</c>-prefixed variable maps onto a
/// configuration key with a single underscore as the path separator —
/// <c>COMUKI_A_B</c> → <c>a:b</c> (binding is case-insensitive, segments
/// are lower-cased for stable keys). Double underscores collapse: an
/// accidental <c>COMUKI_A__B</c> produces the same <c>a:b</c> key instead
/// of an empty segment. The bootstrap's own variables
/// (<c>COMUKI_ENV</c>, <c>COMUKI_CONFIG_PATH</c>) are consumed by the
/// bootstrap itself and never leak into application configuration.
/// </summary>
public sealed class ComukiEnvConfigurationSource(string prefix = "COMUKI_") : IConfigurationSource
{
    /// <summary>The default variable prefix every Comuki host uses.</summary>
    public const string DefaultPrefix = "COMUKI_";

    /// <summary>Bootstrap-owned variables that never map onto configuration keys.</summary>
    public static readonly IReadOnlySet<string> ReservedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ComukiEnvironment.EnvironmentVariable,
        ComukiConfigFile.PathEnvironmentVariable,
    };

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new Provider(prefix);
    }
}

/// <summary>Maps COMUKI_-prefixed variables into flattened configuration keys.</summary>
file sealed class Provider(string prefix) : ConfigurationProvider
{
    public override void Load()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ComukiEnvConfigurationSource.ReservedVariables.Contains(name))
            {
                continue;
            }

            if (ComukiEnvKeyMapper.ToConfigurationKey(name[prefix.Length..]) is not { } key)
            {
                continue;
            }

            Data[key] = entry.Value?.ToString();
        }
    }
}

/// <summary>Single-underscore key mapping: A_B → a:b, empty segments skipped.</summary>
file static class ComukiEnvKeyMapper
{
    public static string? ToConfigurationKey(string rawKey)
    {
        var segments = rawKey.Split('_').Where(static segment => segment.Length > 0);

        var configurationKey = string.Join(':', segments.Select(static segment => segment.ToLowerInvariant()));

        return configurationKey.Length == 0 ? null : configurationKey;
    }
}
