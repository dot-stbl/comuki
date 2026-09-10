using Comuki.Shared.Bootstrap.Config.Toml;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Config;

/// <summary>
/// COMUKI_ env provider (issue #54): single underscore maps to the
/// configuration path separator, unprefixed variables are ignored, and
/// the env values override config.toml. These tests set process env
/// vars — the BootstrapEnvSafe gate keeps them deterministic.
/// </summary>
[Collection("BootstrapEnvSafe")]
public sealed class ComukiEnvConfigurationShould
{
    [Theory(DisplayName = "Given a COMUKI_ variable, when the provider loads, then it maps onto the expected key")]
    [InlineData("COMUKI_SERVER_PORT", "8080", "server:port")]
    [InlineData("COMUKI_BRAIN_GRPCPORT", "17004", "brain:grpcport")]
    [InlineData("COMUKI_TELEMETRY_OTLPENDPOINT", "http://vm:4317", "telemetry:otlpendpoint")]
    [InlineData("COMUKI_HOST_CORS_ALLOWEDORIGINS_0", "http://localhost:17173", "host:cors:allowedorigins:0")]
    public void MapSingleUnderscoreToConfigurationPath(string variable, string value, string expectedKey)
    {
        using var envScope = EnvVarScope.Set((variable, value), (ComukiConfigFile.PathEnvironmentVariable, "/nonexistent/comuki-test.toml"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration[expectedKey].ShouldBe(value);
    }

    [Fact(DisplayName = "Given a COMUKI_A__B variable with a double underscore, when the provider loads, then it collapses to a:b")]
    public void DoubleUnderscoreCollapses()
    {
        using var envScope = EnvVarScope.Set(("COMUKI_HOST__CORS", "true"), (ComukiConfigFile.PathEnvironmentVariable, "/nonexistent/comuki-test.toml"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration["host:cors"].ShouldBe("true");
        configuration.GetSection("host").GetChildren().Count().ShouldBe(1);
    }

    [Fact(DisplayName = "Given unprefixed variables only, when the provider loads, then no keys appear")]
    public void UnprefixedVariablesAreIgnored()
    {
        using var envScope = EnvVarScope.Set(("HOST_CORS", "x"), ("OTHER_HOST", "y"), (ComukiConfigFile.PathEnvironmentVariable, "/nonexistent/comuki-test.toml"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.GetChildren().ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given config.toml with server.port and COMUKI_SERVER_PORT, when both sources load, then the env value wins")]
    public void EnvOverridesToml()
    {
        using var tomlScope = TempTomlScope.Install("""
            [server]
            host = "0.0.0.0"
            port = 8080
            """);
        using var envScope = EnvVarScope.Set(("COMUKI_SERVER_PORT", "18080"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration["server:host"].ShouldBe("0.0.0.0");
        configuration["server:port"].ShouldBe("18080");
    }
}
