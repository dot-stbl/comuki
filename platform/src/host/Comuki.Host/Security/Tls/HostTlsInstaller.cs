using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Comuki.Shared.Bootstrap;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Security.Tls;

/// <summary>
/// Wires <see cref="HostTlsOptions"/> into Kestrel: an HTTPS listener
/// next to the plain-HTTP one plus the HTTP→HTTPS redirect middleware.
/// Registered by <see cref="Program"/> via <see cref="AddComukiTls"/>
/// and by <see cref="HostComposer"/> through <see cref="UseComukiTls"/>
/// — both no-ops while <c>Host:Tls:Enabled</c> is false (the default).
/// <para>
/// The HTTPS listener is registered through <c>UseUrls</c> rather than
/// an explicit <c>ListenAnyIP</c>: the moment code (or the
/// <c>Kestrel:Endpoints</c> config) registers explicit endpoints,
/// Kestrel drops the URL-derived bindings (<c>UseUrls</c> /
/// <c>ASPNETCORE_HTTP_PORTS</c>) and the HTTP listener would silently
/// disappear. Re-registering both addresses keeps the existing
/// resolution chain (<c>[server]</c> host/port → env defaults) intact.
/// </para>
/// </summary>
public static class HostTlsInstaller
{
    /// <summary>HTTP port used when neither <c>[server]</c> port nor the hosting env vars name one.</summary>
    public const int DefaultHttpPort = 8080;

    /// <summary>Wires the TLS options and, when enabled, the dual listener + redirect registration.</summary>
    /// <param name="builder">The host's application builder (Program's <see cref="WebApplicationBuilder"/>).</param>
    /// <returns>The same <paramref name="builder"/> instance, for chaining.</returns>
    public static WebApplicationBuilder AddComukiTls(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<HostTlsOptions>()
            .Bind(builder.Configuration.GetSection(HostTlsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<HostTlsOptions>, HostTlsOptionsValidator>();

        var tls = builder.Configuration.GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();
        if (!tls.Enabled)
        {
            return builder;
        }

        builder.WebHost.UseUrls(ResolveListenerUrls(builder.Configuration, tls));

        if (tls.CertificatePath is { Length: > 0 } certificatePath && tls.CertificateKeyPath is { Length: > 0 } keyPath)
        {
            builder.WebHost.ConfigureKestrel(server => server.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificate = LoadPemCertificate(certificatePath, keyPath);
            }));
        }

        if (ShouldRedirectHttp(tls))
        {
            builder.Services.AddHttpsRedirection(redirect => redirect.HttpsPort = tls.HttpsPort);
        }

        return builder;
    }

    /// <summary>
    /// Installs the HTTP→HTTPS redirect middleware when
    /// <c>Host:Tls:Enabled</c> is on and <c>RedirectHttp</c> is not
    /// false; a no-op otherwise (also when TLS is disabled entirely, so
    /// the composition stays untouched for every existing deployment).
    /// </summary>
    /// <param name="app">The built application, before <c>RunAsync</c>.</param>
    public static void UseComukiTls(this WebApplication app)
    {
        var tls = app.Configuration.GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();
        if (ShouldRedirectHttp(tls))
        {
            app.UseHttpsRedirection();
        }
    }

    /// <summary>
    /// Redirect decision: only a TLS-enabled host redirects, and
    /// <c>null</c> (unset) <see cref="HostTlsOptions.RedirectHttp"/>
    /// counts as enabled — operators must explicitly say
    /// <c>RedirectHttp = false</c> to keep both listeners serving.
    /// </summary>
    /// <param name="options">The bound TLS options.</param>
    /// <returns>Whether plain-HTTP requests should bounce to HTTPS.</returns>
    public static bool ShouldRedirectHttp(HostTlsOptions options)
    {
        return options.Enabled && options.RedirectHttp != false;
    }

    /// <summary>
    /// Resolves the plain-HTTP port the HTTPS listener goes next to:
    /// the comuki-native <c>[server]</c> port (where
    /// <c>COMUKI_SERVER_PORT</c> lands) first, then the container
    /// base-image <c>ASPNETCORE_HTTP_PORTS</c> / <c>HTTP_PORTS</c>
    /// mechanism, then the first <c>http://</c> entry of
    /// <c>ASPNETCORE_URLS</c> / <c>DOTNET_URLS</c>, and finally
    /// <see cref="DefaultHttpPort"/> (8080 — what the shipped containers
    /// listen on).
    /// </summary>
    /// <param name="configuration">Application configuration (comuki sources).</param>
    /// <returns>The port the HTTP listener binds when TLS is enabled.</returns>
    public static int ResolveHttpPort(IConfiguration configuration)
    {
        if (configuration[ComukiBootstrapExtensions.ServerPortKey] is { Length: > 0 } serverPortText
            && int.TryParse(serverPortText, NumberStyles.None, CultureInfo.InvariantCulture, out var serverPort))
        {
            return serverPort;
        }

        var portList = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? Environment.GetEnvironmentVariable("HTTP_PORTS");
        if (portList is { Length: > 0 })
        {
            foreach (var candidate in portList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var envPort))
                {
                    return envPort;
                }
            }
        }

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? Environment.GetEnvironmentVariable("DOTNET_URLS");
        if (urls is { Length: > 0 })
        {
            foreach (var candidate in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var separatorIndex = candidate.LastIndexOf(':');
                if (separatorIndex >= 0
                    && int.TryParse(candidate[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var urlPort))
                {
                    return urlPort;
                }
            }
        }

        return DefaultHttpPort;
    }

    /// <summary>
    /// Resolves the host segment of the listener URLs: the
    /// <c>[server]</c> host when set, <c>*</c> (all interfaces)
    /// otherwise — the same value
    /// <see cref="ComukiBootstrapExtensions.TryResolveServerUrl"/> puts
    /// into its URL.
    /// </summary>
    /// <param name="configuration">Application configuration (comuki sources).</param>
    /// <returns>The host segment of the listener URLs.</returns>
    public static string ResolveListenHost(IConfiguration configuration)
    {
        return configuration[ComukiBootstrapExtensions.ServerHostKey] is { Length: > 0 } configuredHost ? configuredHost : "*";
    }

    /// <summary>
    /// Computes the two listener URLs an enabled TLS section registers:
    /// the plain-HTTP address (host + <see cref="ResolveHttpPort"/>) and
    /// the HTTPS address on <see cref="HostTlsOptions.HttpsPort"/>.
    /// </summary>
    /// <param name="configuration">Application configuration (comuki sources).</param>
    /// <param name="tls">The bound TLS options.</param>
    /// <returns>Exactly two URLs — <c>http://…</c> first, <c>https://…</c> second.</returns>
    public static string[] ResolveListenerUrls(IConfiguration configuration, HostTlsOptions tls)
    {
        var host = ResolveListenHost(configuration);
        var httpPort = ResolveHttpPort(configuration);
        return [$"http://{host}:{httpPort}", $"https://{host}:{tls.HttpsPort}"];
    }

    /// <summary>
    /// Loads a PEM certificate + key pair into an
    /// <see cref="X509Certificate2"/>. On Windows the PEM-loaded key is
    /// not usable by SChannel directly, so the pair is round-tripped
    /// through an in-memory PFX with an exportable machine-store key —
    /// the standard recipe for dev machines; Linux containers take the
    /// direct PEM form.
    /// </summary>
    /// <param name="certificatePath">PEM certificate file (e.g. <c>/certs/tls.crt</c>).</param>
    /// <param name="keyPath">PEM private key file (e.g. <c>/certs/tls.key</c>).</param>
    /// <returns>The server certificate for the HTTPS listener.</returns>
    public static X509Certificate2 LoadPemCertificate(string certificatePath, string keyPath)
    {
        var certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pfx),
                null,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable,
                Pkcs12LoaderLimits.DangerousNoLimits);
        }
    }
}
