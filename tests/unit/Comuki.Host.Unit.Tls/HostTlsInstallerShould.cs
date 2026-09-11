using Comuki.Host.Security.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Tls;

/// <summary>
/// Unit tests for <see cref="HostTlsInstaller"/>: the redirect decision
/// table, the HTTP port resolution chain, the cross-field options
/// validator, and the dual listener registration an enabled TLS section
/// produces on the built application.
/// <para>
/// The environment-mutating tests all live in this class — xUnit runs
/// tests inside one class sequentially, so the env-var write/restore
/// cycles cannot race each other.
/// </para>
/// </summary>
public sealed class HostTlsInstallerShould
{
    [Theory(DisplayName = "Given TLS options, when ShouldRedirectHttp decides, then only enabled + not-explicitly-off redirects")]
    [InlineData(false, null, false)]
    [InlineData(false, true, false)]
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void DecideRedirect(bool enabled, bool? redirectHttp, bool expected)
    {
        var options = new HostTlsOptions { Enabled = enabled, RedirectHttp = redirectHttp };

        HostTlsInstaller.ShouldRedirectHttp(options).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given [server] port in config, when ResolveHttpPort runs, then the comuki-native port wins")]
    public void PreferComukiServerPort()
    {
        var config = BuildConfiguration(("server:port", "18080"));

        WithEnvironment(
            [
                ("ASPNETCORE_HTTP_PORTS", "9090"),
                ("ASPNETCORE_URLS", "http://+:7070"),
            ],
            () => HostTlsInstaller.ResolveHttpPort(config).ShouldBe(18080));
    }

    [Fact(DisplayName = "Given no [server] port but ASPNETCORE_HTTP_PORTS set, when ResolveHttpPort runs, then the first container port wins")]
    public void PreferContainerHttpPortsEnv()
    {
        var config = new ConfigurationBuilder().Build();

        WithEnvironment(
            [
                ("ASPNETCORE_HTTP_PORTS", "8080;8082"),
                ("ASPNETCORE_URLS", "http://+:7070"),
            ],
            () => HostTlsInstaller.ResolveHttpPort(config).ShouldBe(8080));
    }

    [Fact(DisplayName = "Given only ASPNETCORE_URLS, when ResolveHttpPort runs, then the first http entry's port wins")]
    public void PreferFirstHttpUrlsEntry()
    {
        var config = new ConfigurationBuilder().Build();

        WithEnvironment(
            [
                ("ASPNETCORE_HTTP_PORTS", null),
                ("HTTP_PORTS", null),
                ("ASPNETCORE_URLS", "https://+:443;http://+:7070"),
            ],
            () => HostTlsInstaller.ResolveHttpPort(config).ShouldBe(7070));
    }

    [Fact(DisplayName = "Given no port source at all, when ResolveHttpPort runs, then it falls back to the documented 8080")]
    public void FallBackToDefaultPort()
    {
        var config = new ConfigurationBuilder().Build();

        WithEnvironment(
            [
                ("ASPNETCORE_HTTP_PORTS", null),
                ("HTTP_PORTS", null),
                ("ASPNETCORE_URLS", null),
                ("DOTNET_URLS", null),
            ],
            () => HostTlsInstaller.ResolveHttpPort(config).ShouldBe(HostTlsInstaller.DefaultHttpPort));
    }

    [Fact(DisplayName = "Given a certificate path without its key, when the validator runs, then startup fails with a pairing error")]
    public void RejectHalfCertificatePair()
    {
        var result = new HostTlsOptionsValidator().Validate(null, new HostTlsOptions
        {
            Enabled = true,
            CertificatePath = "/certs/tls.crt",
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("together");
    }

    [Fact(DisplayName = "Given UseDevCertificate together with PEM paths, when the validator runs, then startup fails")]
    public void RejectDevCertificateWithPaths()
    {
        var result = new HostTlsOptionsValidator().Validate(null, new HostTlsOptions
        {
            Enabled = true,
            UseDevCertificate = true,
            CertificatePath = "/certs/tls.crt",
            CertificateKeyPath = "/certs/tls.key",
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("UseDevCertificate");
    }

    [Fact(DisplayName = "Given a full PEM pair, when the validator runs, then validation succeeds")]
    public void AcceptFullCertificatePair()
    {
        var result = new HostTlsOptionsValidator().Validate(null, new HostTlsOptions
        {
            Enabled = true,
            CertificatePath = "/certs/tls.crt",
            CertificateKeyPath = "/certs/tls.key",
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given TLS enabled on default config, when ResolveListenerUrls computes the pair, then http 8080 + the configured https port come back")]
    public void RegisterBothListenersWhenEnabled()
    {
        var config = BuildConfiguration(("server:port", "18080"));
        var tls = new HostTlsOptions { Enabled = true, HttpsPort = 8443 };

        var urls = HostTlsInstaller.ResolveListenerUrls(config, tls);

        urls.ShouldBe(["http://*:18080", "https://*:8443"]);
    }

    [Fact(DisplayName = "Given [server] host, when ResolveListenerUrls computes the pair, then both URLs carry the configured host")]
    public void HonorConfiguredListenHost()
    {
        var config = BuildConfiguration(("server:host", "0.0.0.0"));

        var urls = HostTlsInstaller.ResolveListenerUrls(config, new HostTlsOptions { Enabled = true });

        urls.ShouldBe(["http://0.0.0.0:8080", "https://0.0.0.0:8081"]);
    }

    [Fact(DisplayName = "Given TLS disabled (the default), when AddComukiTls wires and the app builds, then composition succeeds without touching listeners")]
    public void AddNothingWhenDisabled()
    {
        using var app = BuildApplication();

        app.Urls.ShouldNotContain(static url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private static WebApplication BuildApplication(params (string Key, string Value)[] pairs)
    {
        return WithCleanHostingEnvironment(() =>
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(
                pairs.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value));
            builder.AddComukiTls();
            return builder.Build();
        });
    }

    private static T WithCleanHostingEnvironment<T>(Func<T> act)
    {
        return WithEnvironment(
            [
                ("ASPNETCORE_HTTP_PORTS", null),
                ("HTTP_PORTS", null),
                ("ASPNETCORE_URLS", null),
                ("DOTNET_URLS", null),
                ("URLS", null),
            ],
            act);
    }

    private static T WithEnvironment<T>((string Name, string? Value)[] assignments, Func<T> act)
    {
        var previous = assignments
            .Select(static assignment => (assignment.Name, Previous: Environment.GetEnvironmentVariable(assignment.Name)))
            .ToArray();

        foreach (var (name, value) in assignments)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            return act();
        }
        finally
        {
            foreach (var (name, previousValue) in previous)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }

    private static void WithEnvironment((string Name, string? Value)[] assignments, Action act)
    {
        var previous = assignments
            .Select(static assignment => (assignment.Name, Previous: Environment.GetEnvironmentVariable(assignment.Name)))
            .ToArray();

        foreach (var (name, value) in assignments)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            act();
        }
        finally
        {
            foreach (var (name, previousValue) in previous)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }
}
