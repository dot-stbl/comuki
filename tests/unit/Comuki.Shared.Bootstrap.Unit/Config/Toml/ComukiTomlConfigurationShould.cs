using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Config.Toml;

/// <summary>
/// TOML → configuration flattening (issue #54): tables nest through
/// colon keys, arrays become indexed keys, and a missing file yields an
/// empty configuration root instead of failing.
/// </summary>
[Collection("BootstrapEnvSafe")]
public sealed class ComukiTomlConfigurationShould
{
    [Fact(DisplayName = "Given config.toml with tables, scalars and arrays, when loaded, then the flattened keys match")]
    public void FlattensTablesScalarsAndArrays()
    {
        using var tomlScope = TempTomlScope.Install("""
            server.host = "0.0.0.0"
            server.port = 8080

            [brain]
            grpcPort = 17004
            enabled = true

            [host.cors]
            allowedOrigins = ["http://localhost:17173", "http://other"]

            [[chat.workers]]
            image = "comuki-worker:local"
            """);

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration["server:host"].ShouldBe("0.0.0.0");
        configuration["server:port"].ShouldBe("8080");
        configuration["brain:grpcport"].ShouldBe("17004");
        configuration["brain:enabled"].ShouldBe("true");
        configuration["host:cors:allowedorigins:0"].ShouldBe("http://localhost:17173");
        configuration["host:cors:allowedorigins:1"].ShouldBe("http://other");
        configuration["chat:workers:0:image"].ShouldBe("comuki-worker:local");
    }

    [Fact(DisplayName = "Given config.toml with a connectionStrings table, when loaded, then GetConnectionString resolves it")]
    public void ConnectionStringsResolve()
    {
        using var tomlScope = TempTomlScope.Install("""
            [connectionStrings]
            comuki = "Host=localhost;Port=5432;Database=comuki;Username=comuki"
            """);

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.GetConnectionString("Comuki").ShouldBe("Host=localhost;Port=5432;Database=comuki;Username=comuki");
    }

    [Fact(DisplayName = "Given no config.toml anywhere, when the provider loads, then the configuration is empty and bootable")]
    public void MissingFileYieldsEmptyConfiguration()
    {
        using var envScope = EnvVarScope.Set(("COMUKI_CONFIG_PATH", "/nonexistent/comuki-test.toml"));

        var configuration = new ConfigurationBuilder().UseComukiConfiguration().Build();

        configuration.GetChildren().ShouldBeEmpty();
    }
}
