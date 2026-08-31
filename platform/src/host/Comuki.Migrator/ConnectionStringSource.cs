using Microsoft.Extensions.Configuration;

namespace Comuki.Migrator;

/// <summary>
/// Connection-string resolution for the Migrator and the design-time factory:
/// <c>COMUKI_DB</c> env var wins, then <c>ConnectionStrings:Comuki</c> from
/// appsettings.json (dev defaults match deploy/docker-compose.yml).
/// </summary>
internal static class ConnectionStringSource
{
    /// <summary>Env var holding the orchestrator database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Returns the resolved connection string, or null when neither source is set.</summary>
    public static string? Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
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
