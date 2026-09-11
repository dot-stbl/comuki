using Comuki.Host.Security.Tls;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Tls;

/// <summary>
/// Unit tests for <see cref="HostTlsOptions"/> binding: defaults on an
/// empty configuration and a full round-trip of the
/// <c>Host:Tls</c> section.
/// </summary>
public sealed class HostTlsOptionsShould
{
    [Fact(DisplayName = "Given empty config, when HostTlsOptions is bound, then TLS is off, https port 8081, no redirect preference, no cert paths")]
    public void ApplyDefaults()
    {
        var config = new ConfigurationBuilder().Build();

        var bound = config.GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();

        bound.Enabled.ShouldBeFalse();
        bound.HttpsPort.ShouldBe(8081);
        bound.RedirectHttp.ShouldBeNull();
        bound.CertificatePath.ShouldBeNull();
        bound.CertificateKeyPath.ShouldBeNull();
        bound.UseDevCertificate.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a full Host:Tls section, when bound, then every field round-trips")]
    public void BindAllFields()
    {
        var config = BuildConfiguration(
            ("Host:Tls:Enabled", "true"),
            ("Host:Tls:HttpsPort", "8443"),
            ("Host:Tls:RedirectHttp", "false"),
            ("Host:Tls:CertificatePath", "/certs/tls.crt"),
            ("Host:Tls:CertificateKeyPath", "/certs/tls.key"),
            ("Host:Tls:UseDevCertificate", "false"));

        var bound = config.GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();

        bound.Enabled.ShouldBeTrue();
        bound.HttpsPort.ShouldBe(8443);
        bound.RedirectHttp.ShouldBe(false);
        bound.CertificatePath.ShouldBe("/certs/tls.crt");
        bound.CertificateKeyPath.ShouldBe("/certs/tls.key");
        bound.UseDevCertificate.ShouldBe(false);
    }

    [Fact(DisplayName = "Given a nullable RedirectHttp, when bound from env-style strings, then true/false/null all parse")]
    public void BindNullableRedirect()
    {
        var enabled = BuildConfiguration(("Host:Tls:RedirectHttp", "true")).GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();
        var disabled = BuildConfiguration(("Host:Tls:RedirectHttp", "false")).GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();
        var unset = BuildConfiguration(("Host:Tls:Enabled", "true")).GetSection(HostTlsOptions.SectionName).Get<HostTlsOptions>() ?? new HostTlsOptions();

        enabled.RedirectHttp.ShouldBe(true);
        disabled.RedirectHttp.ShouldBe(false);
        unset.RedirectHttp.ShouldBeNull();
    }

    [Fact(DisplayName = "Section name stays the Host:Tls wire contract")]
    public void KeepStableSectionName()
    {
        HostTlsOptions.SectionName.ShouldBe("Host:Tls");
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }
}
