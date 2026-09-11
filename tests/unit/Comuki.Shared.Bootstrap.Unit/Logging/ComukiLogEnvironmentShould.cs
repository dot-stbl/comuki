using Comuki.Shared.Bootstrap.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Logging;

/// <summary>
/// Quick env overrides of the log pipeline (issue #56 §4):
/// COMUKI_LOG_LEVEL parses the comuki level words and wins over the
/// [logging] level section; COMUKI_LOG_FORMAT selects the renderer;
/// reserved variables never leak into configuration keys.
/// </summary>
[Collection(nameof(BootstrapEnvSafeCollection))]
public sealed class ComukiLogEnvironmentShould
{
    [Theory(DisplayName = "Given a level word, when parsed, then it maps onto the LogLevel value")]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Critical)]
    [InlineData("fatal", LogLevel.Critical)]
    [InlineData("INFO", LogLevel.Information)]
    public void ParseLevelWords(string text, LogLevel expected)
    {
        ComukiLogEnvironment.ParseLevel(text).ShouldBe(expected);
    }

    [Theory(DisplayName = "Given a blank or unknown level word, when parsed, then it yields null")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("loud")]
    [InlineData("  ")]
    public void ParseUnknownLevelYieldsNull(string? text)
    {
        ComukiLogEnvironment.ParseLevel(text).ShouldBeNull();
    }

    [Fact(DisplayName = "Given COMUKI_LOG_LEVEL set, when the level resolves, then the env value wins over the config section")]
    public void EnvLevelWinsOverConfigSection()
    {
        using var env = EnvVarScope.Set(new EnvVarEntry(ComukiLogEnvironment.LevelVariable, "debug"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ComukiLogEnvironment.ConfigLevelKey] = "error",
        }).Build();

        ComukiLogEnvironment.ResolveLevel(configuration).ShouldBe(LogLevel.Debug);
    }

    [Fact(DisplayName = "Given only the [logging] level section, when the level resolves, then it applies")]
    public void ConfigLevelAppliesWithoutEnvOverride()
    {
        using var env = EnvVarScope.Set(new EnvVarEntry(ComukiLogEnvironment.LevelVariable, null));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ComukiLogEnvironment.ConfigLevelKey] = "warn",
        }).Build();

        ComukiLogEnvironment.ResolveLevel(configuration).ShouldBe(LogLevel.Warning);
    }

    [Fact(DisplayName = "Given neither env nor config level, when the level resolves, then it yields null")]
    public void NoLevelOverrideYieldsNull()
    {
        using var env = EnvVarScope.Set(new EnvVarEntry(ComukiLogEnvironment.LevelVariable, null));

        ComukiLogEnvironment.ResolveLevel(null).ShouldBeNull();
    }

    [Theory(DisplayName = "Given a format word, when parsed, then it maps onto the renderer")]
    [InlineData("text", ComukiLogEnvironment.Format.Text)]
    [InlineData("json", ComukiLogEnvironment.Format.Json)]
    [InlineData("JSON", ComukiLogEnvironment.Format.Json)]
    public void ParseFormatWords(string text, ComukiLogEnvironment.Format expected)
    {
        ComukiLogEnvironment.ParseFormat(text).ShouldBe(expected);
    }

    [Theory(DisplayName = "Given a blank or unknown format, when resolved, then the renderer stays text")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xml")]
    public void UnknownFormatStaysText(string? value)
    {
        ComukiLogEnvironment.ResolveFormat(_ => value).ShouldBe(ComukiLogEnvironment.Format.Text);
    }
}
