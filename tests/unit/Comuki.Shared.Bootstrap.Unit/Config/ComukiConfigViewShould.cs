using Comuki.Shared.Bootstrap.Config;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Config;

/// <summary>
/// Effective-config rendering (issue #56 §1.3): toml + env layers merged
/// into sorted <c>key = value</c> lines, secret-looking keys masked
/// wholesale, connection-string passwords masked inline. Env tests ride
/// the serialized collection — both providers read process-global env.
/// </summary>
[Collection(nameof(BootstrapEnvSafeCollection))]
public sealed class ComukiConfigViewShould
{
    [Fact(DisplayName = "Given toml plus an env override, when rendered, then keys are sorted and env wins")]
    public void RenderSortedKeysWithEnvOverrides()
    {
        using var toml = TempTomlScope.Install("""
            [server]
            port = 18080

            [brain]
            grpcPort = 17004
            """);
        using var env = EnvVarScope.Set(new EnvVarEntry("COMUKI_SERVER_PORT", "18081"));

        var lines = RenderEffectiveConfig();

        lines.ShouldBe(
        [
            "brain:grpcport = 17004",
            "server:port = 18081",
        ]);
    }

    [Fact(DisplayName = "Given a toml with no keys and no env, when rendered, then the output is empty")]
    public void RenderEmptyConfiguration()
    {
        using var toml = TempTomlScope.Install("# empty");

        RenderEffectiveConfig().ShouldBeEmpty();
    }

    [Theory(DisplayName = "Given a key with a secret marker, when the value is rendered, then it is masked wholesale")]
    [InlineData("security:apikey:pepper")]
    [InlineData("Artifacts:SecretKey")]
    [InlineData("auth:bootstrap:adminPassword")]
    [InlineData("translator:workerToken")]
    [InlineData("host:cors:allowedOrigins:0:apiKey")]
    public void MaskWholeValueForSecretKeys(string key)
    {
        ComukiConfigView.RenderValue(key, "hunter2").ShouldBe(ComukiConfigView.SecretMask);
    }

    [Fact(DisplayName = "Given a non-secret key, when the value is rendered, then it passes through verbatim")]
    public void PassThroughNonSecretKeys()
    {
        ComukiConfigView.RenderValue("server:port", "18080").ShouldBe("18080");
        ComukiConfigView.RenderValue("telemetry:otlpendpoint", null).ShouldBe(string.Empty);
    }

    [Fact(DisplayName = "Given a connection string with a password, when rendered, then only the password segment is masked")]
    public void MaskPasswordSegmentOfConnectionStrings()
    {
        var rendered = ComukiConfigView.RenderValue(
            "connectionstrings:comuki",
            "Host=localhost;Port=5432;Database=comuki;Username=comuki;Password=hunter2");

        rendered.ShouldBe("Host=localhost;Port=5432;Database=comuki;Username=comuki;Password=****");
    }

    [Fact(DisplayName = "Given a connection string with a lowercase pwd segment, when rendered, then the segment is masked too")]
    public void MaskPwdSegmentOfConnectionStrings()
    {
        var rendered = ComukiConfigView.RenderValue(
            "ConnectionStrings:Comuki",
            "Host=db;pwd=secret;Timeout=5");

        rendered.ShouldBe("Host=db;pwd=****;Timeout=5");
    }

    [Fact(DisplayName = "Given a secret key under connectionStrings, when rendered, then the secret marker wins over inline masking")]
    public void SecretMarkerWinsUnderConnectionStrings()
    {
        ComukiConfigView.RenderValue("connectionstrings:secretstore", "Host=db").ShouldBe(ComukiConfigView.SecretMask);
    }

    private static IReadOnlyList<string> RenderEffectiveConfig()
    {
        var configuration = new ConfigurationBuilder()
            .UseComukiConfiguration()
            .Build();

        return ComukiConfigView.Render((ConfigurationRoot)configuration);
    }
}
