using System.Globalization;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Config.Toml;
using Comuki.Shared.Bootstrap.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap;

/// <summary>
/// Host-agnostic bootstrap glue (issue #54): replaces the default
/// appsettings/env configuration sources with the comuki ones — optional
/// config.toml (<see cref="ComukiConfigFile"/>) + the COMUKI_ env
/// provider — and offers the one-call console-host bootstrap. The
/// hosting-level DOTNET_/ASPNETCORE_ fallbacks (environment, URLs) are
/// not part of the cleared application sources, so stock tooling like
/// ASPNETCORE_URLS keeps working quietly when no comuki value is set.
/// </summary>
public static class ComukiBootstrapExtensions
{
    /// <summary>Config key holding the listen host ([server] host).</summary>
    public const string ServerHostKey = "server:host";

    /// <summary>Config key holding the listen port ([server] port, COMUKI_SERVER_PORT).</summary>
    public const string ServerPortKey = "server:port";

    /// <summary>
    /// Clears the default application configuration sources (appsettings
    /// JSON, user secrets, bare env vars, command line) and wires the
    /// comuki ones: config.toml first, COMUKI_ env overrides second.
    /// </summary>
    public static IConfigurationBuilder UseComukiConfiguration(this IConfigurationBuilder builder)
    {
        builder.Sources.Clear();
        builder.Add(new ComukiTomlConfigurationSource());
        builder.Add(new ComukiEnvConfigurationSource());
        return builder;
    }

    /// <summary>
    /// One-call bootstrap for console hosts (no web stack): comuki
    /// configuration sources, COMUKI_ENV-driven environment and the
    /// comuki console formatter replacing the default logging providers.
    /// </summary>
    public static HostApplicationBuilder UseComukiBootstrap(this HostApplicationBuilder builder)
    {
        UseComukiConfiguration(builder.Configuration);
        builder.Environment.EnvironmentName = ComukiEnvironment.Resolve();
        builder.Logging.ClearProviders();
        builder.Logging.AddComukiConsole();
        return builder;
    }

    /// <summary>
    /// Resolves the explicit listen URL from [server] host/port — the
    /// COMUKI_SERVER_PORT override lands on the same key through the env
    /// provider. Null when no [server] port is configured, letting the
    /// default Kestrel/ASPNETCORE_URLS mechanism apply.
    /// </summary>
    public static string? TryResolveServerUrl(this IConfiguration configuration)
    {
        if (configuration[ServerPortKey] is not { Length: > 0 } portText
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        var host = configuration[ServerHostKey] is { Length: > 0 } configuredHost ? configuredHost : "*";
        return $"http://{host}:{port}";
    }
}
