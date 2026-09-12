using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap.Logging;

/// <summary>
/// Quick env overrides of the console log pipeline (issue #56 §4).
/// <c>COMUKI_LOG_LEVEL</c> (trace|debug|info|warn|error|fatal) pins the
/// minimum level without touching config.toml and wins over the
/// <c>[logging] level</c> section. <c>COMUKI_LOG_FORMAT</c>
/// (text|json) switches the console renderer — text is the flat comuki
/// line, json emits the same fields as one JSON object per line for
/// collectors. Both are bootstrap-owned: the env provider keeps them out
/// of application configuration. An unparseable value is ignored (the
/// documented values are the contract).
/// </summary>
public static class ComukiLogEnvironment
{
    /// <summary>Env var pinning the minimum log level (wins over [logging] level).</summary>
    public const string LevelVariable = "COMUKI_LOG_LEVEL";

    /// <summary>Env var selecting the console renderer: text (default) or json.</summary>
    public const string FormatVariable = "COMUKI_LOG_FORMAT";

    /// <summary>config.toml key of the [logging] level section.</summary>
    public const string ConfigLevelKey = "logging:level";

    /// <summary>Resolves the minimum level: env override first, then the [logging] level, else null (defaults apply).</summary>
    /// <param name="configuration">The comuki configuration root, when available.</param>
    /// <param name="lookupEnv">Env accessor for tests; defaults to the process env.</param>
    public static LogLevel? ResolveLevel(IConfiguration? configuration, Func<string, string?>? lookupEnv = null)
    {
        var environment = lookupEnv ?? Environment.GetEnvironmentVariable;
        return ParseLevel(environment(LevelVariable)) ?? ParseLevel(configuration?[ConfigLevelKey]);
    }

    /// <summary>Parses a level word (trace|debug|info|warn|warning|error|critical|fatal, case-insensitive); null when blank or unknown.</summary>
    /// <param name="text">Raw value from env or config.</param>
    public static LogLevel? ParseLevel(string? text)
    {
        return text?.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" or "fatal" => LogLevel.Critical,
            _ => null,
        };
    }

    /// <summary>Resolves the console renderer from <see cref="FormatVariable"/>; text when unset or unparseable.</summary>
    /// <param name="lookupEnv">Env accessor for tests; defaults to the process env.</param>
    public static Format ResolveFormat(Func<string, string?>? lookupEnv = null)
    {
        var environment = lookupEnv ?? Environment.GetEnvironmentVariable;
        return ParseFormat(environment(FormatVariable)) ?? Format.Text;
    }

    /// <summary>Parses a format word (text|json, case-insensitive); null when blank or unknown.</summary>
    /// <param name="text">Raw value from env.</param>
    public static Format? ParseFormat(string? text)
    {
        return text?.Trim().ToLowerInvariant() switch
        {
            "text" => Format.Text,
            "json" => Format.Json,
            _ => null,
        };
    }

    /// <summary>The console renderer shapes offered by the bootstrap.</summary>
    public enum Format
    {
        /// <summary>The flat comuki line (default).</summary>
        Text,

        /// <summary>One JSON object per line with the same fields as the text renderer.</summary>
        Json,
    }
}
