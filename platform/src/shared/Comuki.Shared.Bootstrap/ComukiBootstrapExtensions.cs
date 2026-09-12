using System.Globalization;
using Comuki.Shared.Bootstrap.Config;
using Comuki.Shared.Bootstrap.Config.Toml;
using Comuki.Shared.Bootstrap.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap;

/// <summary>
/// Host-agnostic bootstrap glue (issue #54): adds the comuki-native
/// configuration sources on top of the standard ones — optional
/// config.toml (<see cref="ComukiConfigFile"/>) + the COMUKI_ env
/// provider — and offers the one-call console-host bootstrap. Standard
/// sources (appsettings JSON, double-underscore env vars like
/// <c>Artifacts__Endpoint</c>, command line) stay registered, so
/// Helm-style deployments keep working; comuki sources are appended
/// last and therefore win on key collisions.
/// </summary>
public static class ComukiBootstrapExtensions
{
    /// <summary>Config key holding the listen host ([server] host).</summary>
    public const string ServerHostKey = "server:host";

    /// <summary>Config key holding the listen port ([server] port, COMUKI_SERVER_PORT).</summary>
    public const string ServerPortKey = "server:port";

    /// <summary>
    /// Adds the comuki configuration sources on top of whatever is already
    /// registered (appsettings JSON, double-underscore env vars, command
    /// line): config.toml first, COMUKI_ env overrides second. Existing
    /// sources are kept — later sources win, so comuki values override
    /// the standard ones on the same key.
    /// </summary>
    public static IConfigurationBuilder UseComukiConfiguration(this IConfigurationBuilder builder)
    {
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
        builder.Logging.AddComukiConsole(builder.Configuration);
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
