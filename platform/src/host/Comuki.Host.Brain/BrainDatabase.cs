namespace Comuki.Host.Brain;

/// <summary>
/// Connection-string resolution for the brain host — the same order the
/// orchestrator host uses: <c>COMUKI_DB</c> env var, then
/// <c>ConnectionStrings:Comuki</c> from configuration. Throws the setup
/// hint when absent: the brain without memory persistence is useless.
/// </summary>
internal static class BrainDatabase
{
    /// <summary>ConnectionStrings key holding the fallback connection string.</summary>
    public const string ConnectionStringName = "Comuki";

    /// <summary>Env var holding the platform database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Resolves the database connection or throws the setup hint.</summary>
    /// <exception cref="InvalidOperationException">No source holds a connection string.</exception>
    public static string Resolve(IConfiguration configuration)
    {
        return Environment.GetEnvironmentVariable(EnvVariable)
            ?? configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or ConnectionStrings:{ConnectionStringName} in configuration");
    }
}
