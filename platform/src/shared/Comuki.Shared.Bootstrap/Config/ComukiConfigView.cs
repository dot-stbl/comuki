using Microsoft.Extensions.Configuration;

namespace Comuki.Shared.Bootstrap.Config;

/// <summary>
/// Renders the effective comuki configuration — the config.toml layer with
/// COMUKI_* env overrides applied — as sorted flat <c>key = value</c> lines.
/// The engine behind <c>comuki config show</c> (issue #56 §1.3): with the
/// three-layer comuki contract it answers "why did it read that value".
/// Values whose key carries a secret marker (password, secret, pepper,
/// token, key — case-insensitive substring) are masked wholesale;
/// connection-string values keep their shape with the password segment
/// masked inline.
/// </summary>
public static class ComukiConfigView
{
    /// <summary>Mask substituted for whole secret values.</summary>
    public const string SecretMask = "****";

    /// <summary>Key prefix whose values get inline password masking instead of wholesale masking.</summary>
    public const string ConnectionStringsPrefix = "connectionstrings";

    /// <summary>Key markers (compared case-insensitively as substrings of the key) that mask the whole value.</summary>
    public static readonly IReadOnlyList<string> SecretKeyMarkers = ["password", "secret", "pepper", "token", "key"];

    /// <summary>Renders every effective key of the configuration root as sorted <c>key = value</c> lines.</summary>
    /// <param name="configuration">The built comuki configuration root (toml + env layers).</param>
    public static IReadOnlyList<string> Render(IConfigurationRoot configuration)
    {
        // Value-bearing keys only: AsEnumerable also yields the intermediate
        // section placeholders (e.g. "brain" above brain:grpcport) with null
        // values — noise in a flat key = value listing.
        return [.. configuration
            .AsEnumerable()
            .Where(static pair => pair.Value is not null)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key} = {RenderValue(pair.Key, pair.Value)}")];
    }

    /// <summary>Renders one value: masked for secret keys, password-sanitised under connectionStrings, verbatim otherwise.</summary>
    /// <param name="key">Flattened configuration key (e.g. <c>security:apikey:pepper</c>).</param>
    /// <param name="value">Raw effective value, possibly null.</param>
    public static string RenderValue(string key, string? value)
    {
        return IsSecretKey(key)
            ? SecretMask
            : key.StartsWith(ConnectionStringsPrefix, StringComparison.OrdinalIgnoreCase)
            ? ConnectionPasswordMasker.Mask(value)
            : value ?? string.Empty;
    }

    /// <summary>True when the key contains any secret marker (case-insensitive).</summary>
    /// <param name="key">Flattened configuration key.</param>
    public static bool IsSecretKey(string key)
    {
        return SecretKeyMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Masks the Password=/Pwd= segment of a connection string, leaving the rest intact.</summary>
file static class ConnectionPasswordMasker
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var segments = value.Split(';');
        for (var index = 0; index < segments.Length; index++)
        {
            if (PasswordSegment(segments[index]) is { } masked)
            {
                segments[index] = masked;
            }
        }

        return string.Join(';', segments);
    }

    public static string? PasswordSegment(string segment)
    {
        var separator = segment.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        var name = segment[..separator].Trim();
        return name.Equals("Password", StringComparison.OrdinalIgnoreCase) || name.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
            ? $"{segment[..separator]}={ComukiConfigView.SecretMask}"
            : null;
    }
}
