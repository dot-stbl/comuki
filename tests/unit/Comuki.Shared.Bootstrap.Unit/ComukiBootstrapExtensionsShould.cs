using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// Server-URL resolution (issue #54): [server] host/port with the
/// COMUKI_SERVER_PORT override on the same key; null when no port is
/// configured so the default URL mechanism keeps applying.
/// </summary>
[Collection(nameof(BootstrapEnvSafeCollection))]
public sealed class ComukiBootstrapExtensionsShould
{
    [Fact(DisplayName = "Given config.toml with server host and port, when TryResolveServerUrl runs, then the explicit URL comes back")]
    public void ResolvesHostAndPortFromToml()
    {
        using var tomlScope = TempTomlScope.Install("""
            [server]
            host = "127.0.0.1"
            port = 17172
            """);

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.TryResolveServerUrl().ShouldBe("http://127.0.0.1:17172");
    }

    [Fact(DisplayName = "Given only server.port, when TryResolveServerUrl runs, then the host binds all interfaces")]
    public void PortOnlyBindsWildcard()
    {
        using var tomlScope = TempTomlScope.Install("""
            [server]
            port = 8080
            """);

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.TryResolveServerUrl().ShouldBe("http://*:8080");
    }

    [Fact(DisplayName = "Given COMUKI_SERVER_PORT over config.toml, when TryResolveServerUrl runs, then the env override wins")]
    public void EnvPortOverridesToml()
    {
        using var tomlScope = TempTomlScope.Install("""
            [server]
            port = 8080
            """);
        using var envScope = EnvVarScope.Set(new EnvVarEntry("COMUKI_SERVER_PORT", "17172"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.TryResolveServerUrl().ShouldBe("http://*:17172");
    }

    [Fact(DisplayName = "Given no server section at all, when TryResolveServerUrl runs, then null keeps the default URL mechanism")]
    public void NoServerSectionReturnsNull()
    {
        using var envScope = EnvVarScope.Set(new EnvVarEntry("COMUKI_CONFIG_PATH", "/nonexistent/comuki-test.toml"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.TryResolveServerUrl().ShouldBeNull();
    }
}
