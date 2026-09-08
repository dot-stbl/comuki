using Comuki.Migrator.Sources;
using Shouldly;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Resolution chain (<c>COMUKI_DB</c> → <c>COMUKI_DATABASE</c> → appsettings.json)
/// and the Production blank-password guard (issue #21). Each test scopes its
/// own env-var + appsettings.json mutation through <see cref="EnvVarScope"/>
/// and <see cref="AppsettingsScope"/>; the <c>[Collection("MigratorEnvSafe")]</c>
/// gate keeps the two scopes deterministic.
/// </summary>
[Collection("MigratorEnvSafe")]
public sealed class ConnectionStringSourceShould
{
    [Fact(DisplayName = "Given COMUKI_DB is set with a non-empty value, when Resolve runs, then returns the env var verbatim")]
    public void ResolveHonoursPrimaryEnvVar()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, "Host=primary;Database=db1"),
            (ConnectionStringSource.LegacyEnvVariable, "Host=legacy;Database=db2"),
            ("ASPNETCORE_ENVIRONMENT", null),
            ("DOTNET_ENVIRONMENT", null));

        var result = ConnectionStringSource.Resolve();

        result.ShouldBe("Host=primary;Database=db1");
    }

    [Fact(DisplayName = "Given only the legacy COMUKI_DATABASE alias is set, when TryResolve runs, then returns it with fromLegacyAlias=true")]
    public void TryResolveHonoursLegacyAlias()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, null),
            (ConnectionStringSource.LegacyEnvVariable, "Host=legacy;Database=db2"),
            ("ASPNETCORE_ENVIRONMENT", null),
            ("DOTNET_ENVIRONMENT", null));

        var result = ConnectionStringSource.TryResolve(out var fromLegacyAlias);

        result.ShouldBe("Host=legacy;Database=db2");
        fromLegacyAlias.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given appsettings.json has Comuki with a blank password and COMUKI_MIGRATOR_DB_PASSWORD is set, when Resolve runs, then the password is filled from env")]
    public void PasswordIsFilledFromEnv()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, null),
            (ConnectionStringSource.LegacyEnvVariable, null),
            (ConnectionStringSource.PasswordEnvVariable, "secret-from-env"),
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("DOTNET_ENVIRONMENT", "Development"));
        using var appsScope = AppsettingsScope.Install(
                                 /*lang=json,strict*/
                                 """{"ConnectionStrings":{"Comuki":"Host=cfg;Database=db;Username=u;Password="}}""");

        var result = ConnectionStringSource.Resolve();

        result.ShouldNotBeNull();
        result.ShouldContain("Password=secret-from-env");
    }

    [Fact(DisplayName = "Given ASPNETCORE_ENVIRONMENT=Production and appsettings.json has a blank password with no password env var, when Resolve runs, then throws InvalidOperationException")]
    public void ProductionRefusesBlankPassword()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, null),
            (ConnectionStringSource.LegacyEnvVariable, null),
            (ConnectionStringSource.PasswordEnvVariable, null),
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("DOTNET_ENVIRONMENT", null));
        using var appsScope = AppsettingsScope.Install(
                                 /*lang=json,strict*/
                                 """{"ConnectionStrings":{"Comuki":"Host=cfg;Database=db;Username=u;Password="}}""");

        Should.Throw<InvalidOperationException>(static () => ConnectionStringSource.Resolve());
    }

    [Fact(DisplayName = "Given neither env var is set and appsettings.json has no Comuki key, when Resolve runs, then returns null")]
    public void ResolveReturnsNullWhenNoSource()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, null),
            (ConnectionStringSource.LegacyEnvVariable, null),
            (ConnectionStringSource.PasswordEnvVariable, null),
            ("ASPNETCORE_ENVIRONMENT", null),
            ("DOTNET_ENVIRONMENT", null));
        using var appsScope = AppsettingsScope.Install(/*lang=json,strict*/ """{"ConnectionStrings":{}}""");

        var result = ConnectionStringSource.Resolve();

        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given neither env var is set and appsettings.json has no Comuki key, when ResolveOrThrow runs, then throws InvalidOperationException")]
    public void ResolveOrThrowThrowsWithoutSource()
    {
        using var envScope = EnvVarScope.Set(
            (ConnectionStringSource.EnvVariable, null),
            (ConnectionStringSource.LegacyEnvVariable, null),
            (ConnectionStringSource.PasswordEnvVariable, null),
            ("ASPNETCORE_ENVIRONMENT", null),
            ("DOTNET_ENVIRONMENT", null));
        using var appsScope = AppsettingsScope.Install(/*lang=json,strict*/ """{"ConnectionStrings":{}}""");

        Should.Throw<InvalidOperationException>(static () => ConnectionStringSource.ResolveOrThrow());
    }
}
