namespace Comuki.Host;

/// <summary>
/// Connection-string resolution for the orchestrator host — the single
/// reader of the database environment. Resolution order: <c>COMUKI_DB</c>
/// env var, then the legacy <c>COMUKI_DATABASE</c> alias (honored with a
/// startup warning), then <c>ConnectionStrings:Comuki</c> from
/// configuration. The worker runtime wiring in <c>Program</c> and the
/// identity/projects persistence in <see cref="HostComposer"/> both flow
/// from one resolved value — nothing re-reads the environment.
/// </summary>
internal static class HostDatabase
{
    /// <summary>ConnectionStrings key holding the fallback connection string.</summary>
    public const string ConnectionStringName = "Comuki";

    /// <summary>Env var holding the orchestrator database connection string.</summary>
    public const string EnvVariable = "COMUKI_DB";

    /// <summary>Legacy alias of <see cref="EnvVariable"/>; honored with a startup warning.</summary>
    public const string LegacyEnvVariable = "COMUKI_DATABASE";

    /// <summary>A resolved connection string plus its provenance.</summary>
    /// <param name="ConnectionString">The raw connection string shared by every host persistence layer.</param>
    /// <param name="FromLegacyAlias">True when resolved from <see cref="LegacyEnvVariable"/> instead of <see cref="EnvVariable"/>.</param>
    internal sealed record Connection(string ConnectionString, bool FromLegacyAlias);

    /// <summary>
    /// Resolves the database connection or throws the setup hint. Called
    /// exactly once per boot — the callers flow the returned value instead
    /// of resolving again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No source holds a connection string.</exception>
    public static Connection Resolve(IConfiguration configuration)
    {
        return TryResolve(configuration)
            ?? throw new InvalidOperationException(
                $"connection string not found: set the {EnvVariable} env var or ConnectionStrings:{ConnectionStringName} in configuration");
    }

    /// <summary>Resolves the database connection, or null when no source is set.</summary>
    public static Connection? TryResolve(IConfiguration configuration)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new Connection(fromEnvironment, FromLegacyAlias: false);
        }

        var fromLegacyAlias = Environment.GetEnvironmentVariable(LegacyEnvVariable);
        if (!string.IsNullOrWhiteSpace(fromLegacyAlias))
        {
            return new Connection(fromLegacyAlias, FromLegacyAlias: true);
        }

        var fromConfiguration = configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(fromConfiguration)
            ? null
            : new Connection(fromConfiguration, FromLegacyAlias: false);
    }

    /// <summary>Wraps an externally provisioned connection string (e.g. a test container) as a non-legacy resolved connection.</summary>
    public static Connection Explicit(string connectionString)
    {
        return new Connection(connectionString, FromLegacyAlias: false);
    }

    /// <summary>Emits the rename hint through the host logging pipeline when the connection came from the legacy alias.</summary>
    public static void WarnLegacyAlias(Connection connection, ILogger logger)
    {
        if (connection.FromLegacyAlias)
        {
            logger.LogWarning(
                "Database connection resolved from the legacy {LegacyEnvVariable} env var; rename it to {EnvVariable}",
                LegacyEnvVariable,
                EnvVariable);
        }
    }
}
