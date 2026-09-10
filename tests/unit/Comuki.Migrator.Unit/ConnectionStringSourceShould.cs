using Comuki.Migrator.Sources;
using Comuki.Shared.Bootstrap.Config;
using Shouldly;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Resolution chain (<c>COMUKI_DB</c> → <c>COMUKI_DATABASE</c> →
/// config.toml <c>connectionStrings.comuki</c>) and the Production
/// blank-password guard (issue #21) with the COMUKI_ENV-first
/// environment resolution (issue #54). Each test scopes its own env-var
/// + config.toml mutation through <see cref="EnvVarScope"/> and
/// <see cref="TempConfigTomlScope"/>; the <c>[Collection(nameof(MigratorEnvSafeCollection))]</c>
/// gate keeps the two scopes deterministic.
/// </summary>
[Collection(nameof(MigratorEnvSafeCollection))]
public sealed class ConnectionStringSourceShould
{
    [Fact(DisplayName = "Given COMUKI_DB is set with a non-empty value, when Resolve runs, then returns the env var verbatim")]
    public void ResolveHonoursPrimaryEnvVar()
    {
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, "Host=primary;Database=db1"),
            new(ConnectionStringSource.LegacyEnvVariable, "Host=legacy;Database=db2"),
            new(ComukiEnvironment.EnvironmentVariable, null),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, null),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        var result = ConnectionStringSource.Resolve();

        result.ShouldBe("Host=primary;Database=db1");
    }

    [Fact(DisplayName = "Given only the legacy COMUKI_DATABASE alias is set, when TryResolve runs, then returns it with fromLegacyAlias=true")]
    public void TryResolveHonoursLegacyAlias()
    {
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, null),
            new(ConnectionStringSource.LegacyEnvVariable, "Host=legacy;Database=db2"),
            new(ComukiEnvironment.EnvironmentVariable, null),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, null),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        var result = ConnectionStringSource.TryResolve(out var fromLegacyAlias);

        result.ShouldBe("Host=legacy;Database=db2");
        fromLegacyAlias.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given config.toml has connectionStrings.comuki with a blank password and COMUKI_MIGRATOR_DB_PASSWORD is set, when Resolve runs, then the password is filled from env")]
    public void PasswordIsFilledFromEnv()
    {
        using var tomlScope = TempConfigTomlScope.Install("""
            [connectionStrings]
            comuki = "Host=cfg;Database=db;Username=u;Password="
            """);
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, null),
            new(ConnectionStringSource.LegacyEnvVariable, null),
            new(ConnectionStringSource.PasswordEnvVariable, "secret-from-env"),
            new(ComukiEnvironment.EnvironmentVariable, null),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, "Development"),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        var result = ConnectionStringSource.Resolve();

        result.ShouldNotBeNull();
        result.ShouldContain("Password=secret-from-env");
    }

    [Fact(DisplayName = "Given COMUKI_ENV=production with a blank password and no password env var, when Resolve runs, then throws InvalidOperationException")]
    public void ProductionRefusesBlankPasswordViaComukiEnv()
    {
        using var tomlScope = TempConfigTomlScope.Install("""
            [connectionStrings]
            comuki = "Host=cfg;Database=db;Username=u;Password="
            """);
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, null),
            new(ConnectionStringSource.LegacyEnvVariable, null),
            new(ConnectionStringSource.PasswordEnvVariable, null),
            new(ComukiEnvironment.EnvironmentVariable, "production"),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, "Development"),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        Should.Throw<InvalidOperationException>(static () => ConnectionStringSource.Resolve());
    }

    [Fact(DisplayName = "Given neither env var is set and config.toml has no connectionStrings.comuki, when Resolve runs, then returns null")]
    public void ResolveReturnsNullWhenNoSource()
    {
        using var tomlScope = TempConfigTomlScope.Install(/*lang=toml*/ "[server]\nport = 8080");
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, null),
            new(ConnectionStringSource.LegacyEnvVariable, null),
            new(ConnectionStringSource.PasswordEnvVariable, null),
            new(ComukiEnvironment.EnvironmentVariable, null),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, null),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        var result = ConnectionStringSource.Resolve();

        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given neither env var is set and config.toml has no connectionStrings.comuki, when ResolveOrThrow runs, then throws with the config.toml hint")]
    public void ResolveOrThrowThrowsWithoutSource()
    {
        using var tomlScope = TempConfigTomlScope.Install(/*lang=toml*/ "[server]\nport = 8080");
        using var envScope = EnvVarScope.Set(
            new(ConnectionStringSource.EnvVariable, null),
            new(ConnectionStringSource.LegacyEnvVariable, null),
            new(ConnectionStringSource.PasswordEnvVariable, null),
            new(ComukiEnvironment.EnvironmentVariable, null),
            new(ComukiEnvironment.AspNetCoreFallbackVariable, null),
            new(ComukiEnvironment.DotnetFallbackVariable, null));

        var exception = Should.Throw<InvalidOperationException>(static () => ConnectionStringSource.ResolveOrThrow());
        exception.Message.ShouldContain("config.toml");
    }
}
