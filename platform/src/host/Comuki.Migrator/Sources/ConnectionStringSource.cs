using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Config;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Comuki.Migrator.Sources;

/// <summary>
/// Connection-string resolution for the Migrator and the design-time factory:
/// <c>COMUKI_DB</c> env var wins, then the legacy <c>COMUKI_DATABASE</c>
/// alias, then <c>connectionStrings.comuki</c> from config.toml. A blank
/// <c>Password=</c> in the resolved string is filled from
/// <c>COMUKI_MIGRATOR_DB_PASSWORD</c> when set; Production (per
/// <c>COMUKI_ENV</c>, with the quiet ASPNETCORE_/DOTNET_ fallbacks)
/// refuses to start with a blank password (issue #21).
/// </summary>
internal static class ConnectionStringSource
{
    /// <summary>Env var holding the orchestrator database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Legacy alias of <see cref="EnvVariable"/>; honored with a console warning.</summary>
    public const string LegacyEnvVariable = "COMUKI_DATABASE";

    /// <summary>
    /// Env var holding the database password when the connection string
    /// is sourced from config.toml. Empty by contract; deployers set it.
    /// </summary>
    public const string PasswordEnvVariable = "COMUKI_MIGRATOR_DB_PASSWORD";

    /// <summary>Returns the resolved connection string, or null when neither source is set.</summary>
    public static string? Resolve()
    {
        return TryResolve(out _);
    }

    /// <summary>
    /// Returns the resolved connection string and whether it came from the
    /// legacy alias, or null when neither source is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Running in <c>Production</c> with a blank password.
    /// </exception>
    public static string? TryResolve(out bool fromLegacyAlias)
    {
        fromLegacyAlias = false;

        var fromEnv = Environment.GetEnvironmentVariable(EnvVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromLegacyEnv = Environment.GetEnvironmentVariable(LegacyEnvVariable);
        if (!string.IsNullOrWhiteSpace(fromLegacyEnv))
        {
            fromLegacyAlias = true;
            return fromLegacyEnv;
        }

        var configuration = new ConfigurationBuilder()
            .UseComukiConfiguration()
            .Build();

        var connectionString = configuration.GetConnectionString("Comuki");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        connectionString = ConnectionStringSourceGuards.FillPasswordFromEnv(connectionString);
        ConnectionStringSourceGuards.RejectBlankPasswordInProduction(connectionString);

        return connectionString;
    }

    /// <summary>Returns the resolved connection string or throws with a setup hint.</summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static string ResolveOrThrow()
    {
        return Resolve()
            ?? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or connectionStrings.comuki in config.toml");
    }

    /// <summary>
    /// If the connection string's password segment is empty and
    /// <see cref="PasswordEnvVariable"/> is set, replace it with the env
    /// value. Whitespace passwords are left untouched.
    /// </summary>
}

file static class ConnectionStringSourceGuards
{
    public static string FillPasswordFromEnv(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            return connectionString;
        }

        var fromEnv = Environment.GetEnvironmentVariable(ConnectionStringSource.PasswordEnvVariable);
        if (string.IsNullOrWhiteSpace(fromEnv))
        {
            return connectionString;
        }

        builder.Password = fromEnv;
        return builder.ConnectionString;
    }

    /// <summary>
    /// Production hosts must never run with a blank password — the
    /// committed dev default trains the wrong deploy habit. Refuse to
    /// start instead.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static void RejectBlankPasswordInProduction(string connectionString)
    {
        if (!IsProductionEnvironment())
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            return;
        }

        throw new InvalidOperationException(
            $"refusing to start the migrator in Production: {ConnectionStringSource.PasswordEnvVariable} "
            + "is empty and the resolved connection string has no password; "
            + "set the env var or pass a full connection string via "
            + $"{ConnectionStringSource.EnvVariable} before retrying");
    }

    /// <summary>True when COMUKI_ENV (or its quiet ASPNETCORE_/DOTNET_ fallbacks) resolves to Production.</summary>
    public static bool IsProductionEnvironment()
    {
        return string.Equals(ComukiEnvironment.Resolve(), ComukiEnvironment.ProductionName, StringComparison.OrdinalIgnoreCase);
    }
}
