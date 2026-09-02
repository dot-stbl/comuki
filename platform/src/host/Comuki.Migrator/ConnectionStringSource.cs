using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Comuki.Migrator;

/// <summary>
/// Connection-string resolution for the Migrator and the design-time factory:
/// <c>COMUKI_DB</c> env var wins, then the legacy <c>COMUKI_DATABASE</c>
/// alias, then <c>ConnectionStrings:Comuki</c> from appsettings.json.
/// A blank <c>Password=</c> in the resolved string is filled from
/// <c>COMUKI_MIGRATOR_DB_PASSWORD</c> when set; Production refuses to
/// start with a blank password (issue #21).
/// </summary>
internal static class ConnectionStringSource
{
    /// <summary>Env var holding the orchestrator database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Legacy alias of <see cref="EnvVariable"/>; honored with a console warning.</summary>
    public const string LegacyEnvVariable = "COMUKI_DATABASE";

    /// <summary>
    /// Env var holding the database password when the connection string
    /// is sourced from <c>appsettings.json</c>. Empty in the committed
    /// defaults; deployers must set it.
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
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Comuki");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        connectionString = FillPasswordFromEnv(connectionString);
        RejectBlankPasswordInProduction(connectionString);

        return connectionString;
    }

    /// <summary>Returns the resolved connection string or throws with a setup hint.</summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static string ResolveOrThrow()
    {
        return Resolve()
            ?? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or ConnectionStrings:Comuki in appsettings.json");
    }

    /// <summary>
    /// If the connection string's password segment is empty and
    /// <see cref="PasswordEnvVariable"/> is set, replace it with the env
    /// value. Whitespace passwords are left untouched.
    /// </summary>
    private static string FillPasswordFromEnv(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            return connectionString;
        }

        var fromEnv = Environment.GetEnvironmentVariable(PasswordEnvVariable);
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
    private static void RejectBlankPasswordInProduction(string connectionString)
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
            $"refusing to start the migrator in Production: {PasswordEnvVariable} "
            + "is empty and the resolved connection string has no password; "
            + "set the env var or pass a full connection string via "
            + $"{EnvVariable} before retrying");
    }

    /// <summary>True when ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT is "Production".</summary>
    private static bool IsProductionEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }
}
