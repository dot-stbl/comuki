using Microsoft.Extensions.Configuration;

namespace Comuki.Migrator;

/// <summary>
/// Connection-string resolution for the Migrator and the design-time factory:
/// <c>COMUKI_DB</c> env var wins, then the legacy <c>COMUKI_DATABASE</c>
/// alias, then <c>ConnectionStrings:Comuki</c> from appsettings.json
/// (dev defaults match deploy/docker-compose.yml).
/// </summary>
internal static class ConnectionStringSource
{
    /// <summary>Env var holding the orchestrator database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Legacy alias of <see cref="EnvVariable"/>; honored with a console warning.</summary>
    public const string LegacyEnvVariable = "COMUKI_DATABASE";

    /// <summary>Returns the resolved connection string, or null when neither source is set.</summary>
    public static string? Resolve()
    {
        return TryResolve(out _);
    }

    /// <summary>Returns the resolved connection string and whether it came from the legacy alias, or null when neither source is set.</summary>
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

        return configuration.GetConnectionString("Comuki");
    }

    /// <summary>Returns the resolved connection string or throws with a setup hint.</summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static string ResolveOrThrow()
    {
        return Resolve()
            ?? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or ConnectionStrings:Comuki in appsettings.json");
    }
}
