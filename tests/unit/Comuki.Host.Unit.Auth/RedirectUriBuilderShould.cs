using Comuki.Host.Auth;
using Comuki.Host.Auth.PublicHost;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// <see cref="RedirectUriBuilder"/> + <see cref="AuthPublicHostOptions"/>:
/// the OIDC <c>redirect_uri</c> sent to the IdP must come from
/// configuration — never from the inbound <c>Host:</c> header
/// (security audit A03-1 / A10-1). The builder is the single seam; the
/// options class is what production wires into the host DI.
/// </summary>
public sealed class RedirectUriBuilderShould
{
    [Fact(DisplayName = "Given a configured public URL, when the builder runs, then the absolute URL ends with the unified OIDC callback path")]
    public void UsesTheConfiguredPublicUrl()
    {
        var result = RedirectUriBuilder.Build("https://comuki.example.com");

        result.ShouldBe("https://comuki.example.com/api/v1/auth/oidc/callback");
    }

    [Fact(DisplayName = "Given a configured public URL with a trailing slash, when the builder runs, then the callback URL is single-slashed (no double slash)")]
    public void TrimsTrailingSlash()
    {
        var result = RedirectUriBuilder.Build("https://comuki.example.com/");

        result.ShouldBe("https://comuki.example.com/api/v1/auth/oidc/callback");
    }

    [Fact(DisplayName = "Given a missing public URL, when the builder runs, then the host refuses with an explicit configuration message")]
    public void ThrowsWhenPublicUrlIsMissing()
    {
        // The redirect_uri must never be built from the inbound Host
        // header — refusing to start the OIDC flow is the safe answer
        // when no configured public URL is set.
        var exception = Should.Throw<InvalidOperationException>(
            static () => RedirectUriBuilder.Build(""));

        exception.Message.ShouldContain("auth:publicHost:publicUrl");
        exception.Message.ShouldContain("COMUKI_PUBLIC_HOST_URL");
    }

    [Fact(DisplayName = "Given a whitespace public URL, when the builder runs, then the host refuses with the same configuration message")]
    public void ThrowsWhenPublicUrlIsWhitespace()
    {
        Should.Throw<InvalidOperationException>(
            static () => RedirectUriBuilder.Build("   "));
    }

    [Fact(DisplayName = "Given a config that sets auth:publicHost:publicUrl, when AuthPublicHostOptions.Resolve runs, then the value flows through")]
    public void ConfigBindingWinsOverEnv()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["auth:publicHost:publicUrl"] = "https://from-config.example.com",
                ["COMUKI_PUBLIC_HOST_URL"] = "https://from-env.example.com",
            })
            .Build();

        var resolved = AuthPublicHostOptions.Resolve(configuration);

        resolved.PublicUrl.ShouldBe("https://from-config.example.com");
    }

    [Fact(DisplayName = "Given a config that omits auth:publicHost, when Resolve runs with the env var set, then the env var is the fallback")]
    public void EnvVarIsFallbackWhenConfigMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COMUKI_PUBLIC_HOST_URL"] = "https://from-env.example.com",
            })
            .Build();

        var resolved = AuthPublicHostOptions.Resolve(configuration);

        resolved.PublicUrl.ShouldBe("https://from-env.example.com");
    }

    [Fact(DisplayName = "Given neither config nor env var, when Resolve runs, then the resolved URL is empty")]
    public void EmptyWhenNeitherConfigNorEnv()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var resolved = AuthPublicHostOptions.Resolve(configuration);

        resolved.PublicUrl.ShouldBeEmpty();
    }
}
